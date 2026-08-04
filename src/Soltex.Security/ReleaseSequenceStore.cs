namespace Soltex.Security;

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
    private const int MaximumVersionLength = 64;
    private const int ProcessLockTimeoutMilliseconds = 10_000;
    private const int ProcessLockRetryMilliseconds = 50;
    private readonly AuthenticatedJsonStore _store;
    private readonly string _processLockPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ReleaseSequenceStore(string stateRoot)
    {
        string safeStateRoot = NormalizeOrCreateStateRoot(stateRoot);
        _processLockPath = Path.Combine(safeStateRoot, ".release-sequences.lock");
        _store = new AuthenticatedJsonStore(safeStateRoot, "release-sequences");
    }

    public Task<ReleaseSequenceDecision> AcceptVerifiedAsync(
        SignedReleaseVerificationResult verification,
        CancellationToken cancellationToken = default) =>
        EvaluateOrAcceptAsync(verification, mutateState: true, cancellationToken);

    public Task<ReleaseSequenceDecision> EvaluateVerifiedAsync(
        SignedReleaseVerificationResult verification,
        CancellationToken cancellationToken = default) =>
        EvaluateOrAcceptAsync(verification, mutateState: false, cancellationToken);

    public async Task<IReadOnlyList<AcceptedReleaseSequence>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream processLock = await AcquireProcessLockAsync(cancellationToken)
                .ConfigureAwait(false);
            ReleaseSequenceState state = await LoadValidatedStateAsync(cancellationToken)
                .ConfigureAwait(false);
            return state.Channels
                .OrderBy(item => item.Channel, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReleaseSequenceDecision> EvaluateOrAcceptAsync(
        SignedReleaseVerificationResult verification,
        bool mutateState,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(verification);
        ReleaseSequenceDecision? rejected = RejectUnverified(verification, mutateState);
        if (rejected is not null)
        {
            return rejected;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream processLock = await AcquireProcessLockAsync(cancellationToken)
                .ConfigureAwait(false);
            ReleaseSequenceState state = await LoadValidatedStateAsync(cancellationToken)
                .ConfigureAwait(false);
            SignedReleaseManifest manifest = verification.Manifest!;
            AcceptedReleaseSequence? current = state.Channels.SingleOrDefault(item =>
                string.Equals(item.Channel, manifest.Channel, StringComparison.Ordinal));
            if (current is null)
            {
                if (state.Channels.Count >= MaximumChannels)
                {
                    throw new InvalidDataException(
                        "The anti-rollback state contains too many release channels.");
                }

                if (mutateState)
                {
                    state.Channels.Add(CreateAccepted(manifest, verification.ManifestSha256!));
                    await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
                }

                return new ReleaseSequenceDecision(
                    ReleaseSequenceDisposition.AcceptedFirstRelease,
                    manifest.Channel,
                    manifest.Sequence,
                    null,
                    mutateState
                        ? "The first verified release for this channel was accepted."
                        : "The first verified release for this channel would be accepted; state was not changed.");
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
                        ? mutateState
                            ? "The same verified release was already accepted."
                            : "The same verified release is already accepted; state was not changed."
                        : "A different signed manifest reused an already accepted release sequence.");
            }

            if (mutateState)
            {
                int index = state.Channels.IndexOf(current);
                state.Channels[index] = CreateAccepted(manifest, verification.ManifestSha256!);
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }

            return new ReleaseSequenceDecision(
                ReleaseSequenceDisposition.AcceptedUpgrade,
                manifest.Channel,
                manifest.Sequence,
                current.Sequence,
                mutateState
                    ? "The verified release advanced the accepted sequence."
                    : "The verified release would advance the accepted sequence; state was not changed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ReleaseSequenceDecision? RejectUnverified(
        SignedReleaseVerificationResult verification,
        bool mutateState)
    {
        if (verification.Succeeded &&
            verification.Manifest is not null &&
            IsSha256(verification.ManifestSha256))
        {
            return null;
        }

        return new ReleaseSequenceDecision(
            ReleaseSequenceDisposition.RejectedUnverified,
            verification.Manifest?.Channel ?? "unknown",
            verification.Manifest?.Sequence ?? 0,
            null,
            mutateState
                ? "Only a successfully verified signed release may update anti-rollback state."
                : "Only a successfully verified signed release may be evaluated against anti-rollback state.");
    }

    private static AcceptedReleaseSequence CreateAccepted(
        SignedReleaseManifest manifest,
        string manifestSha256) =>
        new(
            manifest.Channel,
            manifest.Sequence,
            manifest.Version,
            manifestSha256,
            DateTimeOffset.UtcNow);

    private async Task<ReleaseSequenceState> LoadValidatedStateAsync(
        CancellationToken cancellationToken)
    {
        ReleaseSequenceState state = await _store.LoadAsync(
            static () => new ReleaseSequenceState(StateSchemaVersion, []),
            cancellationToken).ConfigureAwait(false);
        ValidateState(state);
        return state;
    }

    private async Task<FileStream> AcquireProcessLockAsync(
        CancellationToken cancellationToken)
    {
        long started = Environment.TickCount64;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparseLockFile();
            try
            {
                FileStream stream = new(
                    _processLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                try
                {
                    RejectReparseLockFile();
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch (IOException exception)
            {
                if (Environment.TickCount64 - started >= ProcessLockTimeoutMilliseconds)
                {
                    throw new TimeoutException(
                        "Timed out waiting for the cross-process release-sequence lock.",
                        exception);
                }

                await Task.Delay(ProcessLockRetryMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private void RejectReparseLockFile()
    {
        if (!File.Exists(_processLockPath))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(_processLockPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "A reparse-point release-sequence lock file is not accepted.");
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
                !IsValidVersion(item.Version) ||
                !IsSha256(item.ManifestSha256) ||
                item.AcceptedAtUtc == default ||
                item.AcceptedAtUtc.Offset != TimeSpan.Zero ||
                !channels.Add(item.Channel))
            {
                throw new InvalidDataException("The anti-rollback state contains an invalid entry.");
            }
        }
    }

    private static string NormalizeOrCreateStateRoot(string path)
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
            throw new InvalidDataException(
                "The anti-rollback state directory has no path root.");
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

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Reparse-point state directories are not accepted at this trust boundary.");
            }
        }
    }

    private static bool IsValidChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) &&
        channel.Length <= 32 &&
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
