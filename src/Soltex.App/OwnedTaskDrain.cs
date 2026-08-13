namespace Soltex.App;

internal static class OwnedTaskDrain
{
    internal static async Task<bool> WaitAsync(
        TimeSpan timeout,
        params Task?[] tasks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);

        Task[] owned = tasks
            .Where(task => task is not null)
            .Cast<Task>()
            .Distinct()
            .ToArray();
        if (owned.Length == 0)
        {
            return true;
        }

        Task combined = Task.WhenAll(owned);
        try
        {
            await combined.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception) when (combined.IsCompleted)
        {
            // Faulted and cancelled work is still fully drained. The owner may
            // now dispose the resources that task could have referenced.
        }

        return true;
    }
}
