namespace Soltex.App;

internal sealed class ShutdownCompletedEventArgs(bool resourcesDisposed) : EventArgs
{
    internal bool ResourcesDisposed { get; } = resourcesDisposed;
}
