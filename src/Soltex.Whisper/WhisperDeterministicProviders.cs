using System.Collections.ObjectModel;

namespace Soltex.Whisper;

/// <summary>
/// A capture source that returns a fixed, silent PCM buffer.
/// </summary>
/// <remarks>
/// This exists so the whole pipeline — session lifecycle, text processing, delivery
/// policy, submit gate — can be exercised in CI on a machine with no microphone, and
/// so a developer can work on the Whisper UI without speaking into it. It is not a
/// stand-in for the real WASAPI path and deliberately produces no audible content.
/// </remarks>
public sealed class WhisperDeterministicCaptureSource : IWhisperCaptureSource
{
    private readonly TimeSpan _duration;
    private readonly int _sampleRateHz;

    public WhisperDeterministicCaptureSource(
        TimeSpan? duration = null,
        int sampleRateHz = 16_000)
    {
        _duration = duration ?? TimeSpan.FromSeconds(1);
        _sampleRateHz = sampleRateHz;
    }

    public int CaptureCount { get; private set; }

    public ValueTask<WhisperAudioClip> CaptureAsync(
        WhisperCaptureMode mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CaptureCount++;

        // 16-bit mono silence sized to the requested duration.
        int sampleCount = (int)(_sampleRateHz * _duration.TotalSeconds);
        byte[] pcm = new byte[Math.Max(2, sampleCount * 2)];

        return ValueTask.FromResult(WhisperAudioClip.CreateOwned(
            pcm,
            _sampleRateHz,
            channelCount: 1,
            _duration));
    }
}

/// <summary>
/// A transcriber that replays scripted transcripts in order, then repeats the last
/// one. Records the context it was given so tests can assert that dictionary hints,
/// style, and language actually reach the provider boundary.
/// </summary>
public sealed class WhisperDeterministicTranscriber : IWhisperTranscriber
{
    private readonly ReadOnlyCollection<string> _transcripts;
    private readonly List<WhisperTranscriptionContext> _observedContexts = [];
    private readonly Exception? _failure;
    private int _index;

    public WhisperDeterministicTranscriber(params string[] transcripts)
    {
        ArgumentNullException.ThrowIfNull(transcripts);

        if (transcripts.Length == 0)
        {
            throw new ArgumentException(
                "A deterministic transcriber needs at least one scripted transcript.",
                nameof(transcripts));
        }

        _transcripts = Array.AsReadOnly((string[])transcripts.Clone());
    }

    private WhisperDeterministicTranscriber(Exception failure)
    {
        _failure = failure;
        _transcripts = Array.AsReadOnly(new[] { string.Empty });
    }

    /// <summary>
    /// Builds a transcriber that always fails, for exercising recovery paths.
    /// </summary>
    public static WhisperDeterministicTranscriber CreateFailing(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new WhisperDeterministicTranscriber(new InvalidOperationException(message));
    }

    public ReadOnlyCollection<WhisperTranscriptionContext> ObservedContexts =>
        Array.AsReadOnly(_observedContexts.ToArray());

    public ValueTask<string> TranscribeAsync(
        WhisperAudioClip audio,
        WhisperTranscriptionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        _observedContexts.Add(context);

        if (_failure is not null)
        {
            return ValueTask.FromException<string>(_failure);
        }

        string transcript = _transcripts[Math.Min(_index, _transcripts.Count - 1)];
        _index++;
        return ValueTask.FromResult(transcript);
    }
}

/// <summary>
/// A target inspector that returns a scripted sequence of targets, so focus changing
/// mid-session — the case that must block submission — can be reproduced exactly.
/// </summary>
public sealed class WhisperScriptedTargetInspector : IWhisperTargetInspector
{
    private readonly ReadOnlyCollection<WhisperTargetSnapshot?> _targets;
    private int _index;

    public WhisperScriptedTargetInspector(params WhisperTargetSnapshot?[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length == 0)
        {
            throw new ArgumentException(
                "A scripted inspector needs at least one target.",
                nameof(targets));
        }

        _targets = Array.AsReadOnly((WhisperTargetSnapshot?[])targets.Clone());
    }

    public ValueTask<WhisperTargetSnapshot?> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WhisperTargetSnapshot? target = _targets[Math.Min(_index, _targets.Count - 1)];
        _index++;
        return ValueTask.FromResult(target);
    }
}

/// <summary>
/// A delivery sink that records decisions instead of touching another application.
/// </summary>
public sealed class WhisperRecordingTextDelivery : IWhisperTextDelivery
{
    private readonly List<WhisperDeliveryDecision> _decisions = [];

    public ReadOnlyCollection<WhisperDeliveryDecision> Decisions =>
        Array.AsReadOnly(_decisions.ToArray());

    public ValueTask DeliverAsync(
        WhisperDeliveryDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        _decisions.Add(decision);
        return ValueTask.CompletedTask;
    }
}
