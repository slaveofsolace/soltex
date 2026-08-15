namespace Soltex.App;

internal static class CoreAudioStartupCoordinator
{
    internal static async Task RunWhisperAfterAudioAsync(
        Task audioRefresh,
        Func<Task> whisperRefresh)
    {
        ArgumentNullException.ThrowIfNull(audioRefresh);
        ArgumentNullException.ThrowIfNull(whisperRefresh);

        await audioRefresh.ConfigureAwait(false);
        await whisperRefresh().ConfigureAwait(false);
    }
}
