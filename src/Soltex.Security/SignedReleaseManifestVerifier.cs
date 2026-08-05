using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soltex.Security;

public sealed record SignedReleaseManifest(
    int SchemaVersion,
    string Product,
    string Channel,
    long Sequence,
    string Version,
    DateTimeOffset PublishedAtUtc,
    List<IntegrityManifestFile> Files);

public sealed class SignedReleaseVerificationResult
{
    internal SignedReleaseVerificationResult(
        bool succeeded,
        SignedReleaseManifest? manifest,
        string? manifestSha256,
        int verifiedFileCount,
        IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Manifest = manifest;
        ManifestSha256 = manifestSha256;
        VerifiedFileCount = verifiedFileCount;
        Errors = errors;
    }

    public bool Succeeded { get; }
    public SignedReleaseManifest? Manifest { get; }
    public string? ManifestSha256 { get; }
    public int VerifiedFileCount { get; }
    public IReadOnlyList<string> Errors { get; }
}

public static class SignedReleaseManifestVerifier
{
    private const int SupportedSchemaVersion = 2;
    private const string SupportedProduct = "Soltex";
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const int MaximumSignatureBytes = 16 * 1024;
    private const int MaximumFiles = 10_000;
    private const int MaximumErrors = 64;
    private const int MaximumChannelLength = 32;
    private const int MaximumVersionLength = 64;
    private const int MaximumRelativePathLength = 240;
    private const int MaximumPathDepth = 32;

    private static readonly SearchValues<char> InvalidWindowsNameCharacters = SearchValues.Create(
        new[] { '<', '>', ':', (char)92, (char)34, '|', '?', '*' });
    private static readonly HashSet<string> ReservedWindowsNames = BuildReservedWindowsNames();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<SignedReleaseVerificationResult> VerifyAsync(
        string contentRoot,
        string manifestPath,
        string signaturePath,
        string publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        byte[] manifestBytes;
        byte[] signatureBytes;
        try
        {
            manifestBytes = await ReadBoundedFileAsync(
                manifestPath,
                MaximumManifestBytes,
                "release manifest",
                cancellationToken).ConfigureAwait(false);
            signatureBytes = await ReadBoundedFileAsync(
                signaturePath,
                MaximumSignatureBytes,
                "detached signature",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            return Failure($"The signed release inputs could not be read: {exception.GetType().Name}.");
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (!rsa.VerifyData(
                    manifestBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss))
            {
                return Failure("The signed release manifest signature is invalid.");
            }
        }
        catch (CryptographicException)
        {
            return Failure("The signed release key or signature is invalid.");
        }

        SignedReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SignedReleaseManifest>(manifestBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return Failure("The signed release manifest JSON is invalid.");
        }

        if (manifest is null)
        {
            return Failure("The signed release manifest is empty.");
        }

        string? schemaError = ValidateManifestHeader(manifest);
        if (schemaError is not null)
        {
            return Failure(schemaError);
        }

        string fullRoot;
        try
        {
            fullRoot = NormalizeExistingDirectory(contentRoot);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            return Failure($"The signed content root is unavailable: {exception.GetType().Name}.");
        }

        ErrorCollector errors = new(MaximumErrors);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<IntegrityManifestFile> normalizedFiles = new(manifest.Files.Count);
        int verified = 0;
        foreach (IntegrityManifestFile? item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null)
            {
                errors.Add("The release manifest contains a null file entry.");
                continue;
            }

            if (!TryNormalizeManifestPath(item.Path, out string canonicalPath, out string pathError))
            {
                errors.Add($"{Bound(item.Path)}: {pathError}");
                continue;
            }

            if (!seen.Add(canonicalPath))
            {
                errors.Add($"Duplicate release manifest path: {Bound(canonicalPath)}");
                continue;
            }

            if (item.Length < 0)
            {
                errors.Add($"{Bound(canonicalPath)}: the declared length is invalid.");
                continue;
            }

            if (!IsSha256(item.Sha256))
            {
                errors.Add($"{Bound(canonicalPath)}: the declared SHA-256 is invalid.");
                continue;
            }

            string filePath;
            try
            {
                filePath = NormalizeExistingFileUnderRoot(fullRoot, canonicalPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                errors.Add($"{Bound(canonicalPath)}: {exception.GetType().Name}.");
                continue;
            }

            FileInfo file = new(filePath);
            if (file.Length != item.Length)
            {
                errors.Add($"{Bound(canonicalPath)}: length mismatch.");
                continue;
            }

            string actualHash = await FileHashing.Sha256Async(filePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(actualHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{Bound(canonicalPath)}: SHA-256 mismatch.");
                continue;
            }

            normalizedFiles.Add(new IntegrityManifestFile(
                canonicalPath,
                item.Length,
                item.Sha256.ToUpperInvariant()));
            verified++;
        }

        string manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        bool succeeded = errors.Count == 0;
        SignedReleaseManifest resultManifest = succeeded
            ? manifest with { Files = normalizedFiles }
            : manifest;
        return new SignedReleaseVerificationResult(
            succeeded,
            resultManifest,
            manifestHash,
            verified,
            errors.Items);
    }

    private static string? ValidateManifestHeader(SignedReleaseManifest manifest)
    {
        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            return "The signed release manifest schema is unsupported.";
        }

        if (!string.Equals(manifest.Product, SupportedProduct, StringComparison.Ordinal))
        {
            return "The signed release manifest product identifier is unsupported.";
        }

        if (!IsValidChannel(manifest.Channel))
        {
            return "The signed release channel is invalid.";
        }

        if (manifest.Sequence <= 0)
        {
            return "The signed release sequence must be positive.";
        }

        if (!IsValidVersion(manifest.Version))
        {
            return "The signed release version is outside the accepted bounds.";
        }

        if (manifest.PublishedAtUtc == default ||
            manifest.PublishedAtUtc.Offset != TimeSpan.Zero)
        {
            return "The signed release publication time must be an explicit UTC timestamp.";
        }

        if (manifest.Files is null || manifest.Files.Count is < 1 or > MaximumFiles)
        {
            return $"The signed release manifest must contain between 1 and {MaximumFiles} files.";
        }

        return null;
    }

    private static bool TryNormalizeManifestPath(
        string? path,
        out string canonicalPath,
        out string error)
    {
        canonicalPath = string.Empty;
        error = "the manifest path is not canonical.";
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumRelativePathLength ||
            path.Any(character => character == '\0' || char.IsControl(character)) ||
            path.Contains('\\') ||
            path.StartsWith('/') ||
            path.EndsWith('/') ||
            path.Contains(':') ||
            Path.IsPathRooted(path))
        {
            return false;
        }

        string[] segments = path.Split('/');
        if (segments.Length is < 1 or > MaximumPathDepth)
        {
            return false;
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
                return false;
            }

            string baseName = segment.Split('.')[0];
            if (ReservedWindowsNames.Contains(baseName))
            {
                return false;
            }
        }

        canonicalPath = string.Join('/', segments);
        if (!string.Equals(canonicalPath, path, StringComparison.Ordinal))
        {
            canonicalPath = string.Empty;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) &&
        channel.Length <= MaximumChannelLength &&
        channel[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        channel[^1] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        channel.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');

    private static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version.Length <= MaximumVersionLength &&
        string.Equals(version, version.Trim(), StringComparison.Ordinal) &&
        !version.Any(char.IsControl);

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        string description,
        CancellationToken cancellationToken)
    {
        string fullPath = PathSafety.NormalizeExistingFile(path);
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"The {description} size is outside the accepted bounds.");
        }

        int length = checked((int)stream.Length);
        byte[] content = new byte[length];
        int offset = 0;
        while (offset < content.Length)
        {
            int read = await stream.ReadAsync(
                content.AsMemory(offset, content.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"The {description} ended unexpectedly.");
            }

            offset += read;
        }

        byte[] extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException($"The {description} grew beyond its accepted size.");
        }

        return content;
    }

    private static string NormalizeExistingDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        DirectoryInfo directory = new(fullPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException("The directory does not exist.");
        }

        EnsureExistingDirectoryPathHasNoReparsePoints(fullPath);
        return fullPath;
    }

    private static string NormalizeExistingFileUnderRoot(
        string fullRoot,
        string canonicalRelativePath)
    {
        string candidate = PathSafety.CombineUnderRoot(
            fullRoot,
            canonicalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string relative = Path.GetRelativePath(fullRoot, candidate);
        string current = fullRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Reparse-point path components are not accepted at this trust boundary.");
            }
        }

        return PathSafety.NormalizeExistingFile(candidate);
    }

    private static void EnsureExistingDirectoryPathHasNoReparsePoints(string fullPath)
    {
        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(pathRoot))
        {
            throw new InvalidDataException("The directory has no path root.");
        }

        string relative = Path.GetRelativePath(pathRoot, fullPath);
        string current = pathRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Reparse-point directories are not accepted at this trust boundary.");
            }
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

    private static string Bound(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        return normalized.Length <= MaximumRelativePathLength
            ? normalized
            : normalized[..MaximumRelativePathLength];
    }

    private static SignedReleaseVerificationResult Failure(string error) =>
        new(false, null, null, 0, [error]);

    private sealed class ErrorCollector(int maximum)
    {
        private readonly List<string> _errors = [];
        private int _omitted;

        internal int Count => _errors.Count + _omitted;

        internal IReadOnlyList<string> Items =>
            _omitted == 0
                ? _errors.AsReadOnly()
                : [.. _errors, $"{_omitted} additional verification errors were omitted."];

        internal void Add(string error)
        {
            if (_errors.Count < maximum)
            {
                _errors.Add(error);
            }
            else
            {
                _omitted++;
            }
        }
    }
}
