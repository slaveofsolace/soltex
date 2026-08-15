namespace Soltex.Whisper;

public sealed class WhisperAudioClip
{
    public WhisperAudioClip(
        ReadOnlyMemory<byte> pcm16,
        int sampleRateHz,
        int channelCount,
        TimeSpan duration)
    {
        if (pcm16.IsEmpty)
        {
            throw new ArgumentException("Captured audio cannot be empty.", nameof(pcm16));
        }

        if (sampleRateHz is < 8_000 or > 192_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRateHz),
                "Audio sample rates must be between 8 kHz and 192 kHz.");
        }

        if (channelCount is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount),
                "Whisper capture accepts mono or stereo input.");
        }

        if (duration <= TimeSpan.Zero ||
            duration > WhisperLimits.MaximumHandsFreeDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Audio duration must be greater than zero and no longer than 20 minutes.");
        }

        Pcm16 = pcm16;
        SampleRateHz = sampleRateHz;
        ChannelCount = channelCount;
        Duration = duration;
    }

    public ReadOnlyMemory<byte> Pcm16 { get; }

    public int SampleRateHz { get; }

    public int ChannelCount { get; }

    public TimeSpan Duration { get; }
}

public sealed record WhisperTranscriptionContext(
    WhisperCaptureMode Mode,
    string? PreferredLanguage,
    string ProcessName,
    string StyleName,
    IReadOnlyCollection<string> DictionaryTerms);

public interface IWhisperCaptureSource
{
    ValueTask<WhisperAudioClip> CaptureAsync(
        WhisperCaptureMode mode,
        CancellationToken cancellationToken);
}

public interface IWhisperTranscriber
{
    ValueTask<string> TranscribeAsync(
        WhisperAudioClip audio,
        WhisperTranscriptionContext context,
        CancellationToken cancellationToken);
}

public interface IWhisperTargetInspector
{
    ValueTask<WhisperTargetContext> InspectAsync(
        CancellationToken cancellationToken);
}

public interface IWhisperTextDelivery
{
    ValueTask DeliverAsync(
        WhisperDeliveryDecision decision,
        CancellationToken cancellationToken);
}

public interface IWhisperShortcutHost : IAsyncDisposable
{
    ValueTask RegisterAsync(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutAction, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken);
}
