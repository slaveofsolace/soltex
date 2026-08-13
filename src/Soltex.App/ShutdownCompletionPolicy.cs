namespace Soltex.App;

internal static class ShutdownCompletionPolicy
{
    internal static bool ResourcesWereDisposed(
        bool completionSignaled,
        Task<bool> completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return completionSignaled &&
            completion.Status == TaskStatus.RanToCompletion &&
            completion.Result;
    }
}
