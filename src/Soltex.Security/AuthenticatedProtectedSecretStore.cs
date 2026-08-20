using System.ComponentModel;
using System.Security.Cryptography;

namespace Soltex.Security;

/// <summary>
/// Stores one bounded secret under the current Windows account. DPAPI protects the
/// value and AuthenticatedJsonStore authenticates the complete on-disk envelope.
/// Rotation deletes prior owned generations before committing the replacement.
/// </summary>
public sealed class AuthenticatedProtectedSecretStore : IAsyncDisposable
{
    public const int MaximumSecretBytes = 8_192;

    private const int CurrentVersion = 1;
    private const int MaximumProtectedCharacters = 64_000;
    private readonly AuthenticatedJsonStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AuthenticatedProtectedSecretStore(string stateRoot, string storeName)
    {
        _store = new AuthenticatedJsonStore(stateRoot, storeName);
    }

    internal string StatePath => _store.StatePath;

    internal string BackupPath => _store.BackupPath;

    public async ValueTask<bool> IsAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        using ProtectedSecretLease? lease = await AcquireAsync(cancellationToken)
            .ConfigureAwait(false);
        return lease is not null;
    }

    public async ValueTask<ProtectedSecretLease?> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProtectedSecretDocument document = await _store.LoadAsync(
                static () => new ProtectedSecretDocument(),
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(document.ProtectedSecret))
            {
                if (document.Version != CurrentVersion)
                {
                    throw new InvalidDataException(
                        "Protected secret state has an unsupported format.");
                }

                return null;
            }

            ValidateDocument(document);
            byte[] protectedBytes;
            try
            {
                protectedBytes = Convert.FromBase64String(document.ProtectedSecret);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "Protected secret state contains invalid encoded data.",
                    exception);
            }

            byte[]? clearBytes = null;
            try
            {
                clearBytes = Dpapi.Unprotect(protectedBytes);
                if (clearBytes.Length is < 1 or > MaximumSecretBytes)
                {
                    throw new InvalidDataException(
                        "Protected secret clear data is outside the accepted bounds.");
                }

                return new ProtectedSecretLease(clearBytes);
            }
            catch (Exception exception) when (
                exception is CryptographicException or Win32Exception)
            {
                throw new InvalidDataException(
                    "Protected secret state could not be decrypted for this Windows account.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                if (clearBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(clearBytes);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (secret.Length is < 1 or > MaximumSecretBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secret),
                $"Protected secrets must contain 1 to {MaximumSecretBytes} bytes.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] clearBytes = secret.ToArray();
            byte[]? protectedBytes = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                protectedBytes = Dpapi.Protect(clearBytes);
                string encoded = Convert.ToBase64String(protectedBytes);
                if (encoded.Length > MaximumProtectedCharacters)
                {
                    throw new InvalidDataException(
                        "Protected secret output exceeded its storage bound.");
                }

                ProtectedSecretDocument document = new()
                {
                    ProtectedSecret = encoded
                };

                // Credential rotation is privacy-prioritized: the old current and
                // backup generations are removed before the new secret is written.
                await _store.DeleteStateAsync(cancellationToken).ConfigureAwait(false);
                await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
                if (protectedBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.DeleteStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateDocument(ProtectedSecretDocument document)
    {
        if (document.Version != CurrentVersion ||
            document.ProtectedSecret.Length > MaximumProtectedCharacters)
        {
            throw new InvalidDataException(
                "Protected secret state is outside the accepted format or bounds.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _store.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private sealed class ProtectedSecretDocument
    {
        public int Version { get; set; } = CurrentVersion;

        public string ProtectedSecret { get; set; } = string.Empty;
    }
}

public sealed class ProtectedSecretLease : IDisposable
{
    private byte[]? _secret;

    internal ProtectedSecretLease(ReadOnlySpan<byte> secret)
    {
        _secret = secret.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes =>
        _secret ?? throw new ObjectDisposedException(nameof(ProtectedSecretLease));

    public bool IsDisposed => _secret is null;

    public void Dispose()
    {
        byte[]? secret = Interlocked.Exchange(ref _secret, null);
        if (secret is not null)
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        GC.SuppressFinalize(this);
    }
}
