using System.Security.Cryptography;
using System.Text.Json;

namespace WaveSlate.Security;

internal sealed class AuthenticatedJsonStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataPath;
    private readonly string _macPath;
    private readonly string _keyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _cachedKey;
    private bool _disposed;

    public AuthenticatedJsonStore(string root, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Directory.CreateDirectory(root);
        _dataPath = Path.Combine(root, name + ".json");
        _macPath = Path.Combine(root, name + ".mac");
        _keyPath = Path.Combine(root, "state.key");
    }

    public async Task<T> LoadAsync<T>(Func<T> defaultFactory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(defaultFactory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_dataPath) && !File.Exists(_macPath))
            {
                return defaultFactory();
            }

            if (!File.Exists(_dataPath) || !File.Exists(_macPath))
            {
                throw new InvalidDataException("Authenticated state is incomplete.");
            }

            byte[] data = await File.ReadAllBytesAsync(_dataPath, cancellationToken).ConfigureAwait(false);
            byte[] expectedMac = await File.ReadAllBytesAsync(_macPath, cancellationToken).ConfigureAwait(false);
            byte[] key = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            byte[] actualMac = HMACSHA256.HashData(key, data);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, actualMac))
            {
                throw new InvalidDataException("Authenticated state failed its integrity check.");
            }

            return JsonSerializer.Deserialize<T>(data, JsonOptions)
                ?? throw new InvalidDataException("Authenticated state was empty.");
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
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            byte[] key = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            byte[] mac = HMACSHA256.HashData(key, data);
            await AtomicWriteAsync(_dataPath, data, cancellationToken).ConfigureAwait(false);
            await AtomicWriteAsync(_macPath, mac, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]> GetKeyCopyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] key = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            return key.ToArray();
        }
        finally
        {
            _gate.Release();
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
            byte[] protectedKey = await File.ReadAllBytesAsync(_keyPath, cancellationToken).ConfigureAwait(false);
            _cachedKey = Dpapi.Unprotect(protectedKey);
            if (_cachedKey.Length != 32)
            {
                throw new InvalidDataException("The authenticated-state key has an invalid length.");
            }

            return _cachedKey;
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] protectedBytes = Dpapi.Protect(key);
        await AtomicWriteAsync(_keyPath, protectedBytes, cancellationToken).ConfigureAwait(false);
        _cachedKey = key;
        return _cachedKey;
    }

    private static async Task AtomicWriteAsync(
        string destination,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
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

        _gate.Dispose();
        _disposed = true;
    }
}
