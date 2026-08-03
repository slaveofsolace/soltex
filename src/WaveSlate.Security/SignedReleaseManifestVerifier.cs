using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaveSlate.Security;

public sealed record SignedReleaseManifest(
    int SchemaVersion,
    string Product,
    string Channel,
    long Sequence,
    string Version,
    DateTimeOffset PublishedAtUtc,
    List<IntegrityManifestFile> Files);

public sealed record SignedReleaseVerificationResult(
    bool Succeeded,
    SignedReleaseManifest? Manifest,
    string? ManifestSha256,
    int VerifiedFileCount,
    IReadOnlyList<string> Errors);

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
        int verified = 0;
        foreach (IntegrityManifestFile? item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null)
            {
                errors.Add("The release manifest contains a null file entry.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Path))
            {
                errors.Add("A release manifest file path is empty.");
                continue;
            }

            if (!seen.Add(item.Path))
            {
                errors.Add($"Duplicate release manifest path: {Bound(item.Path)}");
                continue;
            }

            if (item.Length < 0)
            {
                errors.Add($"{Bound(item.Path)}: the declared length is invalid.");
                continue;
            }

            if (!IsSha256(item.Sha256))
            {
                errors.Add($"{Bound(item.Path)}: the declared SHA-256 is invalid.");
                continue;
            }

            string filePath;
            try
            {
                filePath = NormalizeExistingFileUnderRoot(fullRoot, item.Path);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                errors.Add($"{Bound(item.Path)}: {exception.GetType().Name}.");
                continue;
            }

            FileInfo file = new(filePath);
            if (file.Length != item.Length)
            {
                errors.Add($"{Bound(item.Path)}: length mismatch.");
                continue;
            }

            string actualHash = await FileHashing.Sha256Async(filePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(actualHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{Bound(item.Path)}: SHA-256 mismatch.");
                continue;
            }

            verified++;
        }

        string manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        return new SignedReleaseVerificationResult(
            errors.Count == 0,
            manifest,
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

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            manifest.Version.Length > MaximumVersionLength ||
            manifest.Version.Any(char.IsControl))
        {
            return "The signed release version is outside the accepted bounds.";
        }

        if (manifest.Files is null || manifest.Files.Count is < 1 or > MaximumFiles)
        {
            return $"The signed release manifest must contain between 1 and {MaximumFiles} files.";
        }

        return null;
    }

    private static bool IsValidChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) &&
        channel.Length <= MaximumChannelLength &&
        channel.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');

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

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Reparse-point directories are not accepted at this trust boundary.");
        }

        return fullPath;
    }

    private static string NormalizeExistingFileUnderRoot(string fullRoot, string relativePath)
    {
        string candidate = PathSafety.CombineUnderRoot(fullRoot, relativePath);
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

    private static string Bound(string value) =>
        value.Length <= 240 ? value : value[..240];

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

public enum ReleaseSequenceDisposition
{
    AcceptedFirstRelease,
    AcceptedUpgrade,
    AcceptedIdempotent,
    RejectedUnverified,
    RejectedRollback,
    RejectedEquivocation
}

public sealed record ReleaseSequenceDecision(
    ReleaseSequenceDisposition Disposition,
    string Channel,
    long RequestedSequence,
    long? HighestAcceptedSequence,
    string Detail)
{
    public bool Accepted => Disposition is
        ReleaseSequenceDisposition.AcceptedFirstRelease or
        ReleaseSequenceDisposition.AcceptedUpgrade or
        ReleaseSequenceDisposition.AcceptedIdempotent;
}

public sealed record AcceptedReleaseSequence(
    string Channel,
    long Sequence,
    string Version,
    string ManifestSha256,
    DateTimeOffset AcceptedAtUtc);

public sealed class ReleaseSequenceStore : IDisposable
{
    private const int StateSchemaVersion = 1;
    private const int MaximumChannels = 32;
    private readonly AuthenticatedJsonStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ReleaseSequenceStore(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        Directory.CreateDirectory(stateRoot);
        _store = new AuthenticatedJsonStore(stateRoot, "release-sequences");
    }

    public async Task<ReleaseSequenceDecision> AcceptVerifiedAsync(
        SignedReleaseVerificationResult verification,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(verification);
        if (!verification.Succeeded ||
            verification.Manifest is null ||
            !IsSha256(verification.ManifestSha256))
        {
            return new ReleaseSequenceDecision(
                ReleaseSequenceDisposition.RejectedUnverified,
                verification.Manifest?.Channel ?? "unknown",
                verification.Manifest?.Sequence ?? 0,
                null,
                "Only a successfully verified signed release may update anti-rollback state.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleaseSequenceState state = await _store.LoadAsync(
                static () => new ReleaseSequenceState(StateSchemaVersion, []),
                cancellationToken).ConfigureAwait(false);
            ValidateState(state);

            SignedReleaseManifest manifest = verification.Manifest;
            AcceptedReleaseSequence? current = state.Channels.SingleOrDefault(item =>
                string.Equals(item.Channel, manifest.Channel, StringComparison.Ordinal));
            if (current is null)
            {
                if (state.Channels.Count >= MaximumChannels)
                {
                    throw new InvalidDataException(
                        "The anti-rollback state contains too many release channels.");
                }

                state.Channels.Add(new AcceptedReleaseSequence(
                    manifest.Channel,
                    manifest.Sequence,
                    manifest.Version,
                    verification.ManifestSha256!,
                    DateTimeOffset.UtcNow));
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return new ReleaseSequenceDecision(
                    ReleaseSequenceDisposition.AcceptedFirstRelease,
                    manifest.Channel,
                    manifest.Sequence,
                    null,
                    "The first verified release for this channel was accepted.");
            }

            if (manifest.Sequence < current.Sequence)
            {
                return new ReleaseSequenceDecision(
                    ReleaseSequenceDisposition.RejectedRollback,
                    manifest.Channel,
                    manifest.Sequence,
                    current.Sequence,
                    "The verified release sequence is lower than the highest accepted sequence.");
            }

            if (manifest.Sequence == current.Sequence)
            {
                bool sameManifest = string.Equals(
                    verification.ManifestSha256,
                    current.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase);
                return new ReleaseSequenceDecision(
                    sameManifest
                        ? ReleaseSequenceDisposition.AcceptedIdempotent
                        : ReleaseSequenceDisposition.RejectedEquivocation,
                    manifest.Channel,
                    manifest.Sequence,
                    current.Sequence,
                    sameManifest
                        ? "The same verified release was already accepted."
                        : "A different signed manifest reused an already accepted release sequence.");
            }

            int index = state.Channels.IndexOf(current);
            state.Channels[index] = new AcceptedReleaseSequence(
                manifest.Channel,
                manifest.Sequence,
                manifest.Version,
                verification.ManifestSha256!,
                DateTimeOffset.UtcNow);
            await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return new ReleaseSequenceDecision(
                ReleaseSequenceDisposition.AcceptedUpgrade,
                manifest.Channel,
                manifest.Sequence,
                current.Sequence,
                "The verified release advanced the accepted sequence.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AcceptedReleaseSequence>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleaseSequenceState state = await _store.LoadAsync(
                static () => new ReleaseSequenceState(StateSchemaVersion, []),
                cancellationToken).ConfigureAwait(false);
            ValidateState(state);
            return state.Channels
                .OrderBy(item => item.Channel, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateState(ReleaseSequenceState state)
    {
        if (state.SchemaVersion != StateSchemaVersion ||
            state.Channels is null ||
            state.Channels.Count > MaximumChannels)
        {
            throw new InvalidDataException("The anti-rollback state schema is invalid.");
        }

        HashSet<string> channels = new(StringComparer.Ordinal);
        foreach (AcceptedReleaseSequence? item in state.Channels)
        {
            if (item is null ||
                !IsValidChannel(item.Channel) ||
                item.Sequence <= 0 ||
                string.IsNullOrWhiteSpace(item.Version) ||
                item.Version.Length > 64 ||
                !IsSha256(item.ManifestSha256) ||
                !channels.Add(item.Channel))
            {
                throw new InvalidDataException("The anti-rollback state contains an invalid entry.");
            }
        }
    }

    private static bool IsValidChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) &&
        channel.Length <= 32 &&
        channel.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _store.Dispose();
        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record ReleaseSequenceState(
        int SchemaVersion,
        List<AcceptedReleaseSequence> Channels);
}
