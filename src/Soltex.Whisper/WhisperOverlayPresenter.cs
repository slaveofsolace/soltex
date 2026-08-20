using System.Globalization;

namespace Soltex.Whisper;

/// <summary>
/// Every visual state the listening surface can be in.
/// </summary>
public enum WhisperOverlayState
{
    Hidden,
    Listening,
    LockedHandsFree,
    Transcribing,
    Cleaning,
    Ready,
    Inserting,
    Submitted,
    CopiedFallback,
    Cancelled,
    Error
}

/// <summary>
/// The semantic colour role a state maps to. The overlay resolves this to a design
/// token; it never picks a colour directly, so the palette stays in Tokens.xaml.
/// </summary>
public enum WhisperOverlayTone
{
    Neutral,
    Accent,
    Signal,
    Warning,
    Danger
}

/// <summary>
/// Everything the listening overlay needs to render one frame.
/// </summary>
/// <remarks>
/// This is deliberately a plain value produced by a pure function. Keeping the
/// overlay's state machine out of the WPF layer means every visual state — including
/// the error and fallback states that are awkward to reach by hand — is reachable in
/// a test instead of only by dictating into a real application.
/// </remarks>
public sealed record WhisperOverlayView(
    WhisperOverlayState State,
    WhisperOverlayTone Tone,
    string Headline,
    string TargetLabel,
    string? ElapsedText,
    bool ShowElapsed,
    bool ShowLevelMeter,
    bool CancelAvailable,
    string? ActionLabel,
    string Announcement)
{
    public bool IsVisible => State != WhisperOverlayState.Hidden;
}

/// <summary>
/// Inputs the presenter maps to a frame. The level is a live meter value only; no
/// audio is retained to produce it.
/// </summary>
public sealed record WhisperOverlayInputs(
    WhisperSessionSnapshot Session,
    WhisperCaptureMode? Mode,
    string? TargetProcessName,
    bool TargetIsKnown,
    bool HandsFreeLocked,
    TimeSpan Elapsed,
    WhisperDurationState DurationState,
    WhisperDeliveryKind LastDeliveryKind,
    string? ErrorDetail);

public static class WhisperOverlayPresenter
{
    private const string UnknownTarget = "Unknown target";

    public static WhisperOverlayView Project(WhisperOverlayInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Session);

        string target = ResolveTargetLabel(inputs);
        string elapsed = FormatElapsed(inputs.Elapsed);

        return inputs.Session.State switch
        {
            WhisperSessionState.Idle => Hidden(target),
            WhisperSessionState.Listening => Listening(inputs, target, elapsed),
            WhisperSessionState.Transcribing => Working(
                WhisperOverlayState.Transcribing,
                "Transcribing",
                target,
                "Whisper is transcribing your dictation."),
            WhisperSessionState.Processing => Working(
                WhisperOverlayState.Cleaning,
                "Cleaning up",
                target,
                "Whisper is formatting the transcript."),
            WhisperSessionState.Delivering => Working(
                WhisperOverlayState.Inserting,
                "Inserting",
                target,
                $"Whisper is inserting text into {target}."),
            WhisperSessionState.Completed => Completed(inputs, target),
            WhisperSessionState.Cancelled => new WhisperOverlayView(
                WhisperOverlayState.Cancelled,
                WhisperOverlayTone.Neutral,
                "Cancelled",
                target,
                ElapsedText: null,
                ShowElapsed: false,
                ShowLevelMeter: false,
                CancelAvailable: false,
                ActionLabel: null,
                "Dictation cancelled. The previous transcript is unchanged."),
            WhisperSessionState.Faulted => Faulted(inputs, target),
            _ => Hidden(target)
        };
    }

    private static WhisperOverlayView Hidden(string target) => new(
        WhisperOverlayState.Hidden,
        WhisperOverlayTone.Neutral,
        "Whisper is idle",
        target,
        ElapsedText: null,
        ShowElapsed: false,
        ShowLevelMeter: false,
        CancelAvailable: false,
        ActionLabel: null,
        Announcement: string.Empty);

    private static WhisperOverlayView Listening(
        WhisperOverlayInputs inputs,
        string target,
        string elapsed)
    {
        bool handsFree = inputs.Mode == WhisperCaptureMode.HandsFree;
        bool locked = handsFree && inputs.HandsFreeLocked;

        // The 19-minute warning has to be visible while listening, because the session
        // ends on its own at 20 and a silent stop looks like a bug.
        WhisperOverlayTone tone = inputs.DurationState switch
        {
            WhisperDurationState.Expired => WhisperOverlayTone.Danger,
            WhisperDurationState.Warning => WhisperOverlayTone.Warning,
            _ => WhisperOverlayTone.Accent
        };

        string headline = inputs.DurationState switch
        {
            WhisperDurationState.Expired => "Hands-free limit reached",
            WhisperDurationState.Warning => "One minute left",
            _ when locked => "Listening — locked",
            _ when inputs.Mode == WhisperCaptureMode.Command => "Command",
            _ => "Listening"
        };

        return new WhisperOverlayView(
            locked ? WhisperOverlayState.LockedHandsFree : WhisperOverlayState.Listening,
            tone,
            headline,
            target,
            elapsed,
            ShowElapsed: handsFree,
            ShowLevelMeter: true,
            CancelAvailable: true,
            ActionLabel: "Cancel",
            $"{headline}. Dictating into {target}.");
    }

    private static WhisperOverlayView Working(
        WhisperOverlayState state,
        string headline,
        string target,
        string announcement) => new(
        state,
        WhisperOverlayTone.Accent,
        headline,
        target,
        ElapsedText: null,
        ShowElapsed: false,
        ShowLevelMeter: false,
        // Cancellation stays available right up to the irreversible submit step.
        CancelAvailable: state != WhisperOverlayState.Inserting,
        ActionLabel: state != WhisperOverlayState.Inserting ? "Cancel" : null,
        announcement);

    private static WhisperOverlayView Completed(WhisperOverlayInputs inputs, string target)
    {
        return inputs.LastDeliveryKind switch
        {
            WhisperDeliveryKind.InsertAndSubmit or WhisperDeliveryKind.SubmitOnly => new WhisperOverlayView(
                WhisperOverlayState.Submitted,
                WhisperOverlayTone.Signal,
                "Sent",
                target,
                ElapsedText: null,
                ShowElapsed: false,
                ShowLevelMeter: false,
                CancelAvailable: false,
                ActionLabel: null,
                $"Text inserted and submitted in {target}."),
            WhisperDeliveryKind.CopyText => new WhisperOverlayView(
                WhisperOverlayState.CopiedFallback,
                WhisperOverlayTone.Warning,
                "Copied instead",
                target,
                ElapsedText: null,
                ShowElapsed: false,
                ShowLevelMeter: false,
                CancelAvailable: false,
                ActionLabel: "Paste",
                $"Whisper could not insert into {target}, so the text was copied to the clipboard."),
            WhisperDeliveryKind.InsertText => new WhisperOverlayView(
                WhisperOverlayState.Ready,
                WhisperOverlayTone.Signal,
                "Inserted",
                target,
                ElapsedText: null,
                ShowElapsed: false,
                ShowLevelMeter: false,
                CancelAvailable: false,
                ActionLabel: null,
                $"Text inserted into {target}."),
            _ => new WhisperOverlayView(
                WhisperOverlayState.Ready,
                WhisperOverlayTone.Neutral,
                "Nothing to insert",
                target,
                ElapsedText: null,
                ShowElapsed: false,
                ShowLevelMeter: false,
                CancelAvailable: false,
                ActionLabel: null,
                "Whisper produced no text to insert.")
        };
    }

    private static WhisperOverlayView Faulted(WhisperOverlayInputs inputs, string target)
    {
        string detail = inputs.ErrorDetail ?? inputs.Session.LastError ?? "Whisper could not finish.";
        string sanitized = WhisperRedaction.Sanitize(detail, 160);

        return new WhisperOverlayView(
            WhisperOverlayState.Error,
            WhisperOverlayTone.Danger,
            "Whisper stopped",
            target,
            ElapsedText: null,
            ShowElapsed: false,
            ShowLevelMeter: false,
            CancelAvailable: false,
            ActionLabel: "Open Whisper",
            sanitized);
    }

    private static string ResolveTargetLabel(WhisperOverlayInputs inputs)
    {
        if (!inputs.TargetIsKnown || string.IsNullOrWhiteSpace(inputs.TargetProcessName))
        {
            return UnknownTarget;
        }

        return inputs.TargetProcessName.Trim();
    }

    internal static string FormatElapsed(TimeSpan elapsed)
    {
        TimeSpan clamped = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return clamped.TotalHours >= 1
            ? clamped.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : clamped.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
