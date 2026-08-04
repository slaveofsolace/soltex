using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Soltex.Security;

public sealed record ArchiveStagingLimits(
    long MaximumArchiveBytes = 256L * 1024 * 1024,
    int MaximumEntries = 4_096,
    long MaximumEntryBytes = 256L * 1024 * 1024,
    long MaximumTotalExpandedBytes = 1024L * 1024 * 1024,
    double MaximumCompressionRatio = 200,
    int MaximumRelativePathLength = 240,
    int MaximumPathDepth = 32)
{
    internal void Validate()
    {
        if (MaximumArchiveBytes <= 0 ||
            MaximumEntries <= 0 ||
            MaximumEntryBytes <= 0 ||
            MaximumTotalExpandedBytes <= 0 ||
            MaximumEntryBytes > MaximumTotalExpandedBytes ||
            !double.IsFinite(MaximumCompressionRatio) ||
            MaximumCompressionRatio < 1 ||
            MaximumRelativePathLength is < 16 or > 32_000 ||
            MaximumPathDepth is < 1 or > 256)
        {
            throw new InvalidOperationException(
                "One or more archive staging limits are invalid.");
        }
    }
}

public sealed record StagedArchiveEntry(
    string RelativePath,
    long Length,
    string Sha256);

public sealed class StagedArchive : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    internal StagedArchive(string rootPath, IReadOnlyList<StagedArchiveEntry> entries)
    {
        RootPath = rootPath;
        Entries = entries;
    }

    public string RootPath { get; }
    public IReadOnlyList<StagedArchiveEntry> Entries { get; }

    public void Delete()
    {
        if (_disposed)
        {
            return;
        }

        if (Directory.Exists(RootPath))
        {
            BoundedArchiveStager.DeleteStagingTree(RootPath);
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Delete();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class BoundedArchiveStager
{
    private const int CopyBufferBytes = 64 * 1024;
    private static readonly SearchValues<char> InvalidWindowsNameCharacters = SearchValues.Create(
        new[] { '<', '>', ':', (char)92, (char)34, '|', '?', '*' });
    private static readonly HashSet<string> ReservedWindowsNames = BuildReservedWindowsNames();
    private readonly ArchiveStagingLimits _limits;

    public BoundedArchiveStager(ArchiveStagingLimits? limits = null)
    {
        _limits = limits ?? new ArchiveStagingLimits();
        _limits.Validate();
    }

    public async Task<StagedArchive> StageZipAsync(
        string archivePath,
        string stagingParent,
        CancellationToken cancellationToken = default)
    {
        string safeArchivePath = PathSafety.NormalizeExistingFile(archivePath);
        FileInfo archiveInfo = new(safeArchivePath);
        if (archiveInfo.Length is <= 0 || archiveInfo.Length > _limits.MaximumArchiveBytes)
        {
            throw new InvalidDataException("The ZIP archive size is outside the accepted bounds.");
        }

        await using FileStream archiveStream = new(
            safeArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        ZipCentralDirectorySummary directorySummary =
            await ZipCentralDirectoryPreflight.ValidateAsync(
                archiveStream,
                _limits.MaximumEntries,
                cancellationToken).ConfigureAwait(false);

        string safeStagingParent = NormalizeOrCreateStagingParent(stagingParent);
        string stagingRoot = Path.Combine(
            safeStagingParent,
            "soltex-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != directorySummary.EntryCount)
            {
                throw new InvalidDataException(
                    "The ZIP entry count changed between bounded preflight and materialization.");
            }

            Dictionary<string, RegisteredArchivePath> registeredPaths =
                new(StringComparer.OrdinalIgnoreCase);
            List<StagedArchiveEntry> stagedEntries = [];
            long declaredTotal = 0;
            long actualTotal = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool isDirectory = IsDirectory(entry);
                string relativePath = NormalizeEntryPath(entry.FullName, isDirectory);
                RegisterArchivePath(registeredPaths, relativePath, isDirectory);
                RejectUnsupportedEntryType(entry, isDirectory);

                string destination = PathSafety.CombineUnderRoot(
                    stagingRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (isDirectory)
                {
                    Directory.CreateDirectory(destination);
                    EnsureDirectoryChainHasNoReparsePoints(stagingRoot, destination);
                    continue;
                }

                ValidateDeclaredSizeAndRatio(entry);
                declaredTotal = checked(declaredTotal + entry.Length);
                if (declaredTotal > _limits.MaximumTotalExpandedBytes)
                {
                    throw new InvalidDataException(
                        "The ZIP archive exceeds the total expanded-size limit.");
                }

                string? destinationDirectory = Path.GetDirectoryName(destination);
                if (string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    throw new InvalidDataException("A ZIP entry has no staging directory.");
                }

                Directory.CreateDirectory(destinationDirectory);
                EnsureDirectoryChainHasNoReparsePoints(stagingRoot, destinationDirectory);
                (long actualLength, string sha256) = await ExtractEntryAsync(
                    entry,
                    destination,
                    actualTotal,
                    cancellationToken).ConfigureAwait(false);
                actualTotal = checked(actualTotal + actualLength);
                stagedEntries.Add(new StagedArchiveEntry(relativePath, actualLength, sha256));
            }

            return new StagedArchive(stagingRoot, stagedEntries.AsReadOnly());
        }
        catch (OperationCanceledException exception)
        {
            CleanupAfterFailure(stagingRoot, exception);
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            ArgumentException or
            OverflowException)
        {
            CleanupAfterFailure(stagingRoot, exception);
            throw;
        }
    }

    private async Task<(long Length, string Sha256)> ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        long actualTotalBeforeEntry,
        CancellationToken cancellationToken)
    {
        await using Stream input = entry.Open();
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        long actualLength = 0;
        try
        {
            while (true)
            {
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, CopyBufferBytes),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                actualLength = checked(actualLength + read);
                if (actualLength > _limits.MaximumEntryBytes ||
                    actualLength > entry.Length ||
                    checked(actualTotalBeforeEntry + actualLength) >
                    _limits.MaximumTotalExpandedBytes)
                {
                    throw new InvalidDataException(
                        "A ZIP entry expanded beyond its accepted size.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }

            if (actualLength != entry.Length)
            {
                throw new InvalidDataException(
                    "A ZIP entry's actual length did not match its declared length.");
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return (actualLength, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private void ValidateDeclaredSizeAndRatio(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > _limits.MaximumEntryBytes)
        {
            throw new InvalidDataException(
                "A ZIP entry's declared size is outside the accepted bounds.");
        }

        if (entry.CompressedLength < 0)
        {
            throw new InvalidDataException("A ZIP entry has an invalid compressed length.");
        }

        if (entry.Length == 0)
        {
            return;
        }

        if (entry.CompressedLength == 0 ||
            entry.Length / (double)entry.CompressedLength > _limits.MaximumCompressionRatio)
        {
            throw new InvalidDataException(
                "A ZIP entry exceeds the accepted compression-ratio limit.");
        }
    }

    private string NormalizeEntryPath(string entryName, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            entryName.Any(character => character == '\0' || char.IsControl(character)))
        {
            throw new InvalidDataException("A ZIP entry path is empty or contains control characters.");
        }

        string normalized = entryName.Replace('\\', '/');
        if (isDirectory)
        {
            normalized = normalized.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException("A ZIP entry path is rooted or otherwise unsafe.");
        }

        string[] segments = normalized.Split('/');
        if (segments.Length > _limits.MaximumPathDepth ||
            normalized.Length > _limits.MaximumRelativePathLength)
        {
            throw new InvalidDataException("A ZIP entry path exceeds the accepted path bounds.");
        }

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.AsSpan().IndexOfAny(InvalidWindowsNameCharacters) >= 0 ||
                segment.Any(character => character < ' '))
            {
                throw new InvalidDataException("A ZIP entry contains an unsafe path segment.");
            }

            string baseName = segment.Split('.')[0];
            if (ReservedWindowsNames.Contains(baseName))
            {
                throw new InvalidDataException(
                    "A ZIP entry uses a reserved Windows device name.");
            }
        }

        return string.Join('/', segments);
    }

    private static void RegisterArchivePath(
        IDictionary<string, RegisteredArchivePath> registeredPaths,
        string relativePath,
        bool isDirectory)
    {
        string[] segments = relativePath.Split('/');
        string current = string.Empty;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            current = current.Length == 0
                ? segments[index]
                : current + "/" + segments[index];
            if (registeredPaths.TryGetValue(current, out RegisteredArchivePath existing))
            {
                if (existing.Kind != ArchivePathKind.Directory ||
                    !string.Equals(existing.CanonicalPath, current, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "A ZIP file entry collides with a required directory.");
                }
            }
            else
            {
                registeredPaths.Add(
                    current,
                    new RegisteredArchivePath(current, ArchivePathKind.Directory));
            }
        }

        if (registeredPaths.TryGetValue(relativePath, out RegisteredArchivePath finalExisting))
        {
            if (isDirectory &&
                finalExisting.Kind == ArchivePathKind.Directory &&
                string.Equals(
                    finalExisting.CanonicalPath,
                    relativePath,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidDataException(
                "The ZIP archive contains a duplicate or case-colliding path.");
        }

        registeredPaths.Add(
            relativePath,
            new RegisteredArchivePath(
                relativePath,
                isDirectory ? ArchivePathKind.Directory : ArchivePathKind.File));
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        string.IsNullOrEmpty(entry.Name) ||
        entry.FullName.EndsWith('/') ||
        entry.FullName.EndsWith('\\');

    private static void RejectUnsupportedEntryType(ZipArchiveEntry entry, bool isDirectory)
    {
        int dosAttributes = entry.ExternalAttributes & 0xFFFF;
        if ((dosAttributes & (int)FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("ZIP reparse-point entries are not accepted.");
        }

        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        int unixFileType = unixMode & 0xF000;
        if (unixFileType == 0xA000)
        {
            throw new InvalidDataException("ZIP symbolic-link entries are not accepted.");
        }

        if (unixFileType != 0 &&
            unixFileType != (isDirectory ? 0x4000 : 0x8000))
        {
            throw new InvalidDataException("The ZIP entry type is not a regular file or directory.");
        }
    }

    private static string NormalizeOrCreateStagingParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        EnsureExistingDirectoryPathHasNoReparsePoints(fullPath);
        Directory.CreateDirectory(fullPath);
        EnsureExistingDirectoryPathHasNoReparsePoints(fullPath);
        return fullPath;
    }

    private static void EnsureExistingDirectoryPathHasNoReparsePoints(string fullPath)
    {
        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(pathRoot))
        {
            throw new InvalidDataException("The staging directory has no path root.");
        }

        string relative = Path.GetRelativePath(pathRoot, fullPath);
        string current = pathRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                continue;
            }

            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("A reparse-point staging directory is not accepted.");
            }
        }
    }

    private static void EnsureDirectoryChainHasNoReparsePoints(
        string stagingRoot,
        string directoryPath)
    {
        string relative = Path.GetRelativePath(stagingRoot, directoryPath);
        if (relative == ".")
        {
            return;
        }

        string current = stagingRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "A reparse point appeared inside the archive staging directory.");
            }
        }
    }

    internal static void DeleteStagingTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        FileAttributes rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(root);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            if (isDirectory && !isReparsePoint)
            {
                DeleteStagingTree(entry);
            }
            else if (isDirectory)
            {
                Directory.Delete(entry);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }

        Directory.Delete(root);
    }

    private static void CleanupAfterFailure(string root, Exception originalFailure)
    {
        try
        {
            DeleteStagingTree(root);
        }
        catch (Exception cleanupFailure) when (
            cleanupFailure is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                "Archive staging failed and its private staging directory could not be removed.",
                new AggregateException(originalFailure, cleanupFailure));
        }
    }

    private static HashSet<string> BuildReservedWindowsNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL"
        };
        for (int index = 1; index <= 9; index++)
        {
            string suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            names.Add("COM" + suffix);
            names.Add("LPT" + suffix);
        }

        return names;
    }

    private readonly record struct RegisteredArchivePath(
        string CanonicalPath,
        ArchivePathKind Kind);

    private enum ArchivePathKind
    {
        File,
        Directory
    }
}
