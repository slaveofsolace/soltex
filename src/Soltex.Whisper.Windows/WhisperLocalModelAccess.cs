namespace Soltex.Whisper.Windows;

/// <summary>
/// Internal runtime-only access to a verified local model. UI policy receives only
/// <see cref="IWhisperModelManager"/> and never observes this path-bearing lease.
/// </summary>
internal interface IWhisperLocalModelSource
{
    ValueTask<WhisperVerifiedModelLease> OpenVerifiedAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Holds a non-delete-sharing read handle while the native runtime opens the same
/// verified model path. This closes the ordinary replacement window between digest
/// verification and the eager native model load without copying the model into a
/// second managed buffer.
/// </summary>
internal sealed class WhisperVerifiedModelLease : IAsyncDisposable
{
    private FileStream? _ownershipHandle;

    internal WhisperVerifiedModelLease(
        string modelPath,
        FileStream ownershipHandle,
        WindowsWhisperFileIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(ownershipHandle);
        ModelPath = modelPath;
        _ownershipHandle = ownershipHandle;
        Identity = identity;
    }

    internal string ModelPath { get; }

    internal WindowsWhisperFileIdentity Identity { get; }

    public async ValueTask DisposeAsync()
    {
        FileStream? handle = Interlocked.Exchange(ref _ownershipHandle, null);
        if (handle is not null)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
