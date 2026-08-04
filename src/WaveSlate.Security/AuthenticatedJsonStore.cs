using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaveSlate.Security;

internal sealed class AuthenticatedJsonStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = StrictJson.CreateSerializerOptions();

    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly string _legacyMacPath;
    private readonly string _keyPath;
    private readonly byte[] _domain;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _cachedKey;
    private bool _disposed;

    public AuthenticatedJsonStore(string root, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string normalizedName = NormalizeStoreName(name);
        string fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);
        _statePath = Path.Combine(fullRoot, normalizedName + ".json");
        _backupPath = _statePath + ".bak";
        _legacyMacPath = Path.Combine(fullRoot, normalizedName + ".mac");
        _keyPath = Path.Combine(fullRoot, "state.key");
        _domain = Encoding.UTF8.GetBytes(
            "Soltex.AuthenticatedJsonStore/" + normalizedName + "/v1");
    }

    internal string StatePath => _statePath;
    internal string BackupPath => _backupPath;

    public async Task<T> LoadAsync<T>(
        Func<T> defaultFactory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(defaultFactory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AuthenticatedStateSnapshot<T> snapshot = await LoadCoreAsync(
                defaultFactory,
                cancellationToken).ConfigureAwait(false);
            return snapshot.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AuthenticatedStateSnapshot<T> current = await LoadCoreAsync(
                () => value,
                cancellationToken).ConfigureAwait(false);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            StrictJson.ValidateNoDuplicateProperties(payload);
            byte[] key = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            byte[] envelope = AuthenticatedStateEnvelope.Build(
                _domain,
                checked(current.Generation + 1),
                payload,
                key);
            await CommitEnvelopeAsync(
                envelope,
                current.RawEnvelope,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<byte[]> GetKeyCopyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal byte[] CreateEnvelopeForTesting(
        ulong generation,
        byte[] payload,
        byte[] key) =>
        AuthenticatedStateEnvelope.Build(_domain, generation, payload, key);

    private async Task<AuthenticatedStateSnapshot<T>> LoadCoreAsync<T>(
        Func<T> defaultFactory,
        CancellationToken cancellationToken)
    {
        bool stateExists = File.Exists(_statePath);
        bool backupExists = File.Exists(_backupPath);
        bool legacyMacExists = File.Exists(_legacyMacPath);
        if (!stateExists && !backupExists && !legacyMacExists)
        {
            return new AuthenticatedStateSnapshot<T>(defaultFactory(), 0, null);
        }

        byte[] key = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedEnvelopeCandidate<T> primary = await ReadCandidateAsync<T>(
            _statePath,
            key,
            cancellationToken).ConfigureAwait(false);
        AuthenticatedEnvelopeCandidate<T> backup = await ReadCandidateAsync<T>(
            _backupPath,
            key,
            cancellationToken).ConfigureAwait(false);

        if (!primary.Valid &&
            primary.Kind == AuthenticatedEnvelopeKind.NotEnvelope &&
            !backup.Valid)
        {
            if (!legacyMacExists)
            {
                throw new InvalidDataException(
                    "Authenticated state exists but is not a verifiable envelope or complete legacy state.");
            }

            return await MigrateLegacyAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }

        if (!primary.Valid && !backup.Valid)
        {
            throw new InvalidDataException(
                "No authenticated current or last-known-good state generation could be verified.");
        }

        AuthenticatedEnvelopeCandidate<T> selected;
        bool repairPrimary;
        if (primary.Valid && backup.Valid)
        {
            if (primary.Generation == backup.Generation &&
                !string.Equals(
                    primary.PayloadSha256,
                    backup.PayloadSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Authenticated state generations equivocate at the same sequence.");
            }

            selected = primary.Generation >= backup.Generation ? primary : backup;
            repairPrimary = selected == backup;
        }
        else
        {
            selected = primary.Valid ? primary : backup;
            repairPrimary = !primary.Valid;
        }

        if (repairPrimary)
        {
            await RepairAsync(_statePath, selected.RawEnvelope!, cancellationToken)
                .ConfigureAwait(false);
        }

        if (legacyMacExists && primary.Valid && !backup.Valid)
        {
            if (backup.Kind == AuthenticatedEnvelopeKind.NotEnvelope)
            {
                await PromoteLegacyBackupAsync<T>(primary, key, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await RepairAsync(_backupPath, primary.RawEnvelope!, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (legacyMacExists)
        {
            TryDeleteLegacyMac();
        }

        return new AuthenticatedStateSnapshot<T>(
            selected.Value!,
            selected.Generation,
            selected.RawEnvelope);
    }

    private async Task<AuthenticatedEnvelopeCandidate<T>> ReadCandidateAsync<T>(
        string path,
        byte[] key,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return AuthenticatedEnvelopeCandidate<T>.Missing();
        }

        try
        {
            byte[] raw = await AuthenticatedStateFileIO.ReadBoundedAsync(
                path,
                AuthenticatedStateEnvelope.MaximumEnvelopeBytes,
                cancellationToken).ConfigureAwait(false);
            return AuthenticatedStateEnvelope.HasMagic(raw)
                ? AuthenticatedStateEnvelope.Parse<T>(raw, _domain, key, JsonOptions)
                : AuthenticatedEnvelopeCandidate<T>.NotEnvelope();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return AuthenticatedEnvelopeCandidate<T>.Invalid();
        }
    }

    private async Task<AuthenticatedStateSnapshot<T>> MigrateLegacyAsync<T>(
        byte[] key,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath) || !File.Exists(_legacyMacPath))
        {
            throw new InvalidDataException("Legacy authenticated state is incomplete.");
        }

        byte[] payload = await AuthenticatedStateFileIO.ReadBoundedAsync(
            _statePath,
            AuthenticatedStateEnvelope.MaximumPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        byte[] expectedMac = await AuthenticatedStateFileIO.ReadBoundedAsync(
            _legacyMacPath,
            AuthenticatedStateEnvelope.LegacyMacLength,
            cancellationToken).ConfigureAwait(false);
        if (expectedMac.Length != AuthenticatedStateEnvelope.LegacyMacLength ||
            !CryptographicOperations.FixedTimeEquals(
                expectedMac,
                HMACSHA256.HashData(key, payload)))
        {
            throw new InvalidDataException(
                "Legacy authenticated state failed its integrity check.");
        }

        T value = StrictJson.Deserialize<T>(payload, JsonOptions);
        byte[] envelope = AuthenticatedStateEnvelope.Build(_domain, 1, payload, key);
        await ReplaceLegacyAsync(envelope, cancellationToken).ConfigureAwait(false);
        await RepairAsync(_backupPath, envelope, cancellationToken).ConfigureAwait(false);
        TryDeleteLegacyMac();
        return new AuthenticatedStateSnapshot<T>(value, 1, envelope);
    }

    private async Task PromoteLegacyBackupAsync<T>(
        AuthenticatedEnvelopeCandidate<T> primary,
        byte[] key,
        CancellationToken cancellationToken)
    {
        byte[] payload = await AuthenticatedStateFileIO.ReadBoundedAsync(
            _backupPath,
            AuthenticatedStateEnvelope.MaximumPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        byte[] expectedMac = await AuthenticatedStateFileIO.ReadBoundedAsync(
            _legacyMacPath,
            AuthenticatedStateEnvelope.LegacyMacLength,
            cancellationToken).ConfigureAwait(false);
        if (expectedMac.Length != AuthenticatedStateEnvelope.LegacyMacLength ||
            !CryptographicOperations.FixedTimeEquals(
                expectedMac,
                HMACSHA256.HashData(key, payload)))
        {
            throw new InvalidDataException(
                "The last-known-good legacy state could not be verified during migration recovery.");
        }

        _ = StrictJson.Deserialize<T>(payload, JsonOptions);
        string payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.Equals(payloadHash, primary.PayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy migration state does not match the authenticated current generation.");
        }

        byte[] envelope = AuthenticatedStateEnvelope.Build(
            _domain,
            primary.Generation,
            payload,
            key);
        await RepairAsync(_backupPath, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitEnvelopeAsync(
        byte[] envelope,
        byte[]? previousEnvelope,
        CancellationToken cancellationToken)
    {
        if (previousEnvelope is not null)
        {
            await RepairAsync(_backupPath, previousEnvelope, cancellationToken)
                .ConfigureAwait(false);
        }

        await RepairAsync(_statePath, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceLegacyAsync(
        byte[] envelope,
        CancellationToken cancellationToken) =>
        await RepairAsync(_statePath, envelope, cancellationToken).ConfigureAwait(false);

    private static async Task RepairAsync(
        string destination,
        byte[] verifiedEnvelope,
        CancellationToken cancellationToken)
    {
        string temporary = AuthenticatedStateFileIO.CreateTemporaryPath(destination);
        try
        {
            await AuthenticatedStateFileIO.WriteDurableAsync(
                temporary,
                verifiedEnvelope,
                cancellationToken).ConfigureAwait(false);
            AuthenticatedStateFileIO.RejectReparsePoint(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            AuthenticatedStateFileIO.TryDelete(temporary);
        }
    }

    private async Task<byte[]> GetOrCreateKeyAsync(CancellationToken cancellationToken)
    {
        if (_cachedKey is not null)
        {
            return _cachedKey;
        }

        if (File.Exists(_keyPath))
        {
            _cachedKey = await ReadAndUnprotectKeyAsync(cancellationToken).ConfigureAwait(false);
            return _cachedKey;
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] protectedBytes = Dpapi.Protect(key);
        string temporary = AuthenticatedStateFileIO.CreateTemporaryPath(_keyPath);
        try
        {
            await AuthenticatedStateFileIO.WriteDurableAsync(
                temporary,
                protectedBytes,
                cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, _keyPath);
                _cachedKey = key;
                return _cachedKey;
            }
            catch (IOException) when (File.Exists(_keyPath))
            {
                CryptographicOperations.ZeroMemory(key);
                _cachedKey = await ReadAndUnprotectKeyAsync(cancellationToken)
                    .ConfigureAwait(false);
                return _cachedKey;
            }
        }
        finally
        {
            AuthenticatedStateFileIO.TryDelete(temporary);
        }
    }

    private async Task<byte[]> ReadAndUnprotectKeyAsync(CancellationToken cancellationToken)
    {
        AuthenticatedStateFileIO.RejectReparsePoint(_keyPath);
        byte[] protectedKey = await AuthenticatedStateFileIO.ReadBoundedAsync(
            _keyPath,
            16 * 1024,
            cancellationToken).ConfigureAwait(false);
        byte[] key = Dpapi.Unprotect(protectedKey);
        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("The authenticated-state key has an invalid length.");
        }

        return key;
    }

    private static string NormalizeStoreName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Length > 64 ||
            normalized.Any(character =>
                char.IsControl(character) ||
                Path.GetInvalidFileNameChars().Contains(character)))
        {
            throw new ArgumentException("The authenticated store name is invalid.", nameof(name));
        }

        return normalized;
    }

    private void TryDeleteLegacyMac()
    {
        try
        {
            AuthenticatedStateFileIO.RejectReparsePoint(_legacyMacPath);
            AuthenticatedStateFileIO.TryDelete(_legacyMacPath);
        }
        catch (InvalidDataException)
        {
            // The verified envelope remains authoritative; a reparse legacy MAC is inert.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_cachedKey is not null)
        {
            CryptographicOperations.ZeroMemory(_cachedKey);
            _cachedKey = null;
        }

        CryptographicOperations.ZeroMemory(_domain);
        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record AuthenticatedStateSnapshot<T>(
        T Value,
        ulong Generation,
        byte[]? RawEnvelope);
}
