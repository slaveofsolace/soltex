using System.Security.Cryptography;

namespace Soltex.Whisper;

/// <summary>
/// An owned, disposable PCM buffer. Disposal clears the exact backing array before
/// releasing it so completed, failed, or cancelled provider work does not leave a
/// second unmanaged lifetime for captured speech.
/// </summary>
public sealed class WhisperAudioClip : IDisposable
{
    private byte[]? _pcm16;

    public WhisperAudioClip(
        ReadOnlyMemory<byte> pcm16,
        int sampleRateHz,
        int channelCount,
        TimeSpan duration)
    {
        Validate(pcm16.Length, sampleRateHz, channelCount, duration);
        _pcm16 = pcm16.ToArray();
        SampleRateHz = sampleRateHz;
        ChannelCount = channelCount;
        Duration = duration;
    }

    private WhisperAudioClip(
        byte[] pcm16,
        int sampleRateHz,
        int channelCount,
        TimeSpan duration,
        bool takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(pcm16);
        Validate(pcm16.Length, sampleRateHz, channelCount, duration);

        _pcm16 = takeOwnership ? pcm16 : (byte[])pcm16.Clone();
        SampleRateHz = sampleRateHz;
        ChannelCount = channelCount;
        Duration = duration;
    }

    /// <summary>
    /// Transfers ownership of <paramref name="pcm16"/> into a clip. The caller must
    /// not retain or mutate the array and must dispose the returned clip.
    /// </summary>
    public static WhisperAudioClip CreateOwned(
        byte[] pcm16,
        int sampleRateHz,
        int channelCount,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(pcm16);
        try
        {
            return new WhisperAudioClip(
                pcm16,
                sampleRateHz,
                channelCount,
                duration,
                takeOwnership: true);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(pcm16);
            throw;
        }
    }

    public ReadOnlyMemory<byte> Pcm16 =>
        _pcm16 ?? throw new ObjectDisposedException(nameof(WhisperAudioClip));

    public int SampleRateHz { get; }

    public int ChannelCount { get; }

    public TimeSpan Duration { get; }

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _pcm16, null);
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static void Validate(
        int byteLength,
        int sampleRateHz,
        int channelCount,
        TimeSpan duration)
    {
        if (byteLength == 0)
        {
            throw new ArgumentException("Captured audio cannot be empty.");
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
    }
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
    /// <summary>
    /// Returns one atomic identity-and-capability snapshot of the focused control.
    /// A null result means the target could not be established safely.
    /// </summary>
    ValueTask<WhisperTargetSnapshot?> InspectAsync(
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
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken);
}
