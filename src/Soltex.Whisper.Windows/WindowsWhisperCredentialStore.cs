using Soltex.Security;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Current-user DPAPI credential adapter for one transcription provider. The
/// provider id is content-free and determines an isolated authenticated state file.
/// </summary>
public sealed class WindowsWhisperCredentialStore : IWhisperCredentialStore
{
    private readonly AuthenticatedProtectedSecretStore _store;
    private bool _disposed;

    public WindowsWhisperCredentialStore(string stateRoot, string providerId)
    {
        ProviderId = WhisperProviderStatus.ValidateProviderId(providerId);
        _store = new AuthenticatedProtectedSecretStore(
            stateRoot,
            "whisper-credential-" + ProviderId);
    }

    public string ProviderId { get; }

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _store.IsAvailableAsync(cancellationToken);
    }

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _store.SaveAsync(credential, cancellationToken);
    }

    public async ValueTask<WhisperCredentialLease?> AcquireAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using ProtectedSecretLease? protectedLease = await _store.AcquireAsync(cancellationToken)
            .ConfigureAwait(false);
        return protectedLease is null
            ? null
            : new WhisperCredentialLease(protectedLease.Bytes.Span);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _store.DeleteAsync(cancellationToken);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _store.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
