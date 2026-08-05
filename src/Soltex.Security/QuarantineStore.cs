namespace Soltex.Security;

public sealed class QuarantineStore : IDisposable
{
    private const int MaximumEntries = 2_000;
    private readonly string _root;
    private readonly string _filesRoot;
    private readonly AuthenticatedJsonStore _indexStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal QuarantineStore(string root, AuthenticatedJsonStore indexStore)
    {
        _root = Path.GetFullPath(root);
        _filesRoot = Path.Combine(_root, "files");
        Directory.CreateDirectory(_filesRoot);
        _indexStore = indexStore;
    }

    public async Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        QuarantineIndex index = await _indexStore.LoadAsync(
            static () => new QuarantineIndex([]),
            cancellationToken).ConfigureAwait(false);
        return index.Entries.OrderByDescending(entry => entry.QuarantinedAtUtc).ToArray();
    }

    public async Task<QuarantineEntry> QuarantineAsync(
        string path,
        string detection,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(detection);
        string source = PathSafety.NormalizeExistingFile(path);
        if (PathSafety.IsUnderRoot(_root, source))
        {
            throw new InvalidOperationException("The file is already inside Soltex quarantine.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<QuarantineEntry> entries = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (entries.Count >= MaximumEntries)
            {
                throw new InvalidOperationException("Quarantine has reached its safety limit.");
            }

            FileInfo sourceInfo = new(source);
            string hash = await FileHashing.Sha256Async(source, cancellationToken).ConfigureAwait(false);
            Guid id = Guid.NewGuid();
            string storedFileName = id.ToString("N") + ".blocked";
            string destination = Path.Combine(_filesRoot, storedFileName);
            QuarantineEntry entry = new(
                id,
                source,
                storedFileName,
                hash,
                sourceInfo.Length,
                Limit(detection, 500),
                DateTimeOffset.UtcNow);

            await MoveVerifiedAsync(source, destination, hash, cancellationToken).ConfigureAwait(false);
            try
            {
                entries.Add(entry);
                await _indexStore.SaveAsync(new QuarantineIndex(entries), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await MoveVerifiedAsync(destination, source, hash, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<QuarantineEntry> entries = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
            QuarantineEntry entry = entries.SingleOrDefault(candidate => candidate.Id == id)
                ?? throw new KeyNotFoundException("The quarantine item was not found.");

            string storedPath = PathSafety.CombineUnderRoot(_filesRoot, entry.StoredFileName);
            string storedHash = await FileHashing.Sha256Async(storedPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(storedHash, entry.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The quarantined file failed its integrity check.");
            }

            string destination = GetRestoreDestination(entry.OriginalPath);
            string? destinationDirectory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidDataException("The original quarantine path is invalid.");
            }

            Directory.CreateDirectory(destinationDirectory);
            await MoveVerifiedAsync(storedPath, destination, entry.Sha256, cancellationToken).ConfigureAwait(false);
            entries.Remove(entry);
            await _indexStore.SaveAsync(new QuarantineIndex(entries), cancellationToken).ConfigureAwait(false);
            return destination;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<QuarantineEntry> entries = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
            QuarantineEntry entry = entries.SingleOrDefault(candidate => candidate.Id == id)
                ?? throw new KeyNotFoundException("The quarantine item was not found.");
            string storedPath = PathSafety.CombineUnderRoot(_filesRoot, entry.StoredFileName);
            if (File.Exists(storedPath))
            {
                File.Delete(storedPath);
            }

            entries.Remove(entry);
            await _indexStore.SaveAsync(new QuarantineIndex(entries), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetRestoreDestination(string originalPath)
    {
        string normalized = Path.GetFullPath(originalPath);
        if (PathSafety.IsUnderRoot(_root, normalized))
        {
            throw new InvalidDataException("The original path points inside quarantine.");
        }

        if (!File.Exists(normalized))
        {
            return normalized;
        }

        string directory = Path.GetDirectoryName(normalized)
            ?? throw new InvalidDataException("The original path has no directory.");
        string fileName = Path.GetFileNameWithoutExtension(normalized);
        string extension = Path.GetExtension(normalized);
        return Path.Combine(
            directory,
            $"{fileName}.restored-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{extension}");
    }

    private static async Task MoveVerifiedAsync(
        string source,
        string destination,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            throw new IOException("The destination file already exists.");
        }

        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            await using (FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string copiedHash = await FileHashing.Sha256Async(destination, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(copiedHash, expectedHash, StringComparison.Ordinal))
            {
                File.Delete(destination);
                throw new InvalidDataException("The quarantine copy failed verification.");
            }

            File.Delete(source);
        }

        string movedHash = await FileHashing.Sha256Async(destination, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(movedHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The moved file failed verification.");
        }
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private sealed record QuarantineIndex(List<QuarantineEntry> Entries);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
