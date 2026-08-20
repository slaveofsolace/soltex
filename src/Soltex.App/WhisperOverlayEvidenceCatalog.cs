using Soltex.Whisper;

namespace Soltex.App;

/// <summary>
/// Fixed, content-free presenter inputs for native overlay evidence. These states
/// never open capture, transcription, clipboard, target, or submission adapters.
/// </summary>
internal static class WhisperOverlayEvidenceCatalog
{
    internal static IReadOnlyList<string> StateIds { get; } = Array.AsReadOnly(
    [
        "listening",
        "command",
        "hands-free-locked",
        "hands-free-warning",
        "hands-free-expired",
        "transcribing",
        "cleaning",
        "inserting",
        "inserted",
        "submitted",
        "copied-fallback",
        "cancelled",
        "error"
    ]);

    internal static bool TryProject(string stateId, out WhisperOverlayView? view)
    {
        view = null;
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return false;
        }

        WhisperOverlayInputs? inputs = stateId.ToLowerInvariant() switch
        {
            "listening" => Inputs(WhisperSessionState.Listening),
            "command" => Inputs(
                WhisperSessionState.Listening,
                WhisperCaptureMode.Command),
            "hands-free-locked" => Inputs(
                WhisperSessionState.Listening,
                WhisperCaptureMode.HandsFree,
                handsFreeLocked: true,
                elapsed: TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(12)),
            "hands-free-warning" => Inputs(
                WhisperSessionState.Listening,
                WhisperCaptureMode.HandsFree,
                durationState: WhisperDurationState.Warning,
                elapsed: TimeSpan.FromMinutes(19)),
            "hands-free-expired" => Inputs(
                WhisperSessionState.Listening,
                WhisperCaptureMode.HandsFree,
                durationState: WhisperDurationState.Expired,
                elapsed: TimeSpan.FromMinutes(20)),
            "transcribing" => Inputs(WhisperSessionState.Transcribing),
            "cleaning" => Inputs(WhisperSessionState.Processing),
            "inserting" => Inputs(WhisperSessionState.Delivering),
            "inserted" => Inputs(
                WhisperSessionState.Completed,
                deliveryKind: WhisperDeliveryKind.InsertText),
            "submitted" => Inputs(
                WhisperSessionState.Completed,
                deliveryKind: WhisperDeliveryKind.InsertAndSubmit),
            "copied-fallback" => Inputs(
                WhisperSessionState.Completed,
                deliveryKind: WhisperDeliveryKind.CopyText),
            "cancelled" => Inputs(WhisperSessionState.Cancelled),
            "error" => Inputs(
                WhisperSessionState.Faulted,
                lastError: "Microphone access needs attention."),
            _ => null
        };
        if (inputs is null)
        {
            return false;
        }

        view = WhisperOverlayPresenter.Project(inputs);
        return view.IsVisible;
    }

    private static WhisperOverlayInputs Inputs(
        WhisperSessionState state,
        WhisperCaptureMode mode = WhisperCaptureMode.PushToTalk,
        bool handsFreeLocked = false,
        WhisperDurationState durationState = WhisperDurationState.Current,
        TimeSpan? elapsed = null,
        WhisperDeliveryKind deliveryKind = WhisperDeliveryKind.None,
        string? lastError = null) => new(
        new WhisperSessionSnapshot(
            state,
            mode,
            DateTimeOffset.UnixEpoch,
            lastError),
        mode,
        TargetProcessName: "Sample editor",
        TargetIsKnown: true,
        HandsFreeLocked: handsFreeLocked,
        Elapsed: elapsed ?? TimeSpan.FromSeconds(12),
        DurationState: durationState,
        LastDeliveryKind: deliveryKind,
        ErrorDetail: lastError);
}
