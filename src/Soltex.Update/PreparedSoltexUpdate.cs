using Soltex.Security;

namespace Soltex.Update;

public sealed class PreparedSoltexUpdate : IAsyncDisposable, IDisposable
{
    private readonly StagedArchive _stagedArchive;
    private readonly AcquiredUpdateBundle _acquiredBundle;
    private bool _disposed;

    internal PreparedSoltexUpdate(
        SoltexUpdatePreview preview,
        StagedArchive stagedArchive,
        AcquiredUpdateBundle acquiredBundle)
    {
        Preview = preview;
        _stagedArchive = stagedArchive;
        _acquiredBundle = acquiredBundle;
    }

    public SoltexUpdatePreview Preview { get; }
    public string VerifiedStagingRoot => _stagedArchive.RootPath;

    public bool ConfirmPreview(
        string planSha256,
        string phrase,
        DateTimeOffset nowUtc) =>
        Preview.Confirmation.IsSatisfiedBy(planSha256, phrase, nowUtc);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stagedArchive.Dispose();
        _acquiredBundle.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stagedArchive.DisposeAsync().ConfigureAwait(false);
        await _acquiredBundle.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
