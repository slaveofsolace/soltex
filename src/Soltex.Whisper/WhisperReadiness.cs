using System.Collections.ObjectModel;

namespace Soltex.Whisper;

/// <summary>
/// Whether a prerequisite is satisfied, needs the user, or is broken.
/// </summary>
public enum WhisperReadinessState
{
    Ready,
    NeedsSetup,
    Blocked
}

/// <summary>
/// One prerequisite, phrased as something the user can act on.
/// </summary>
/// <remarks>
/// Each check carries exactly one next action. That is the whole point: a user who
/// presses the shortcut and gets nothing should be able to open Whisper and read the
/// single sentence that explains why, rather than compare a wall of toggles.
/// </remarks>
public sealed class WhisperReadinessCheck
{
    public WhisperReadinessCheck(
        string id,
        string title,
        WhisperReadinessState state,
        string detail,
        string? nextAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Id = id.Trim();
        Title = title.Trim();
        State = state;
        Detail = detail.Trim();
        NextAction = string.IsNullOrWhiteSpace(nextAction) ? null : nextAction.Trim();
    }

    public string Id { get; }

    public string Title { get; }

    public WhisperReadinessState State { get; }

    /// <summary>One sentence explaining the current state.</summary>
    public string Detail { get; }

    /// <summary>The single next action, or <see langword="null"/> when nothing is needed.</summary>
    public string? NextAction { get; }
}

/// <summary>
/// The observable facts the readiness report is built from. Supplied by the host so
/// the report itself stays deterministic and testable.
/// </summary>
public sealed record WhisperReadinessInputs(
    bool FeatureEnabled,
    bool MicrophoneSelected,
    bool MicrophonePermissionGranted,
    bool ShortcutsRegistered,
    string? ShortcutRegistrationError,
    bool TranscriberConfigured,
    bool TranscriberCredentialAvailable,
    bool TargetInspectionAvailable,
    bool AutoSendEnabled,
    bool AutoSendWarningAccepted,
    int EnabledAutoSendProfileCount);

/// <summary>
/// A complete, ordered readiness report plus the one thing to do next.
/// </summary>
public sealed class WhisperReadinessReport
{
    internal WhisperReadinessReport(IEnumerable<WhisperReadinessCheck> checks)
    {
        Checks = Array.AsReadOnly(checks.ToArray());
        CanDictate = Checks
            .Where(check => RequiredForDictation.Contains(check.Id, StringComparer.Ordinal))
            .All(check => check.State == WhisperReadinessState.Ready);
        PrimaryBlocker = Checks.FirstOrDefault(check => check.State == WhisperReadinessState.Blocked)
            ?? Checks.FirstOrDefault(check => check.State == WhisperReadinessState.NeedsSetup);
    }

    private static readonly string[] RequiredForDictation =
        ["enabled", "microphone", "shortcuts", "transcriber"];

    public ReadOnlyCollection<WhisperReadinessCheck> Checks { get; }

    /// <summary>True only when every prerequisite for dictating is satisfied.</summary>
    public bool CanDictate { get; }

    /// <summary>The single check to surface first, or <see langword="null"/> when all are ready.</summary>
    public WhisperReadinessCheck? PrimaryBlocker { get; }

    /// <summary>
    /// A short status line for the Whisper page header and the listening overlay.
    /// </summary>
    public string Summary => CanDictate
        ? "Whisper is ready to dictate."
        : PrimaryBlocker?.Detail ?? "Whisper is not ready.";
}

public static class WhisperReadinessEvaluator
{
    public static WhisperReadinessReport Evaluate(WhisperReadinessInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        List<WhisperReadinessCheck> checks =
        [
            EvaluateEnabled(inputs),
            EvaluateMicrophone(inputs),
            EvaluateShortcuts(inputs),
            EvaluateTranscriber(inputs),
            EvaluateTargetInspection(inputs),
            EvaluateAutoSend(inputs)
        ];

        return new WhisperReadinessReport(checks);
    }

    private static WhisperReadinessCheck EvaluateEnabled(WhisperReadinessInputs inputs) =>
        inputs.FeatureEnabled
            ? new WhisperReadinessCheck(
                "enabled",
                "Whisper",
                WhisperReadinessState.Ready,
                "Whisper is turned on.",
                nextAction: null)
            : new WhisperReadinessCheck(
                "enabled",
                "Whisper",
                WhisperReadinessState.NeedsSetup,
                "Whisper is turned off, so no shortcut will start dictation.",
                "Turn Whisper on");

    private static WhisperReadinessCheck EvaluateMicrophone(WhisperReadinessInputs inputs)
    {
        if (!inputs.MicrophonePermissionGranted)
        {
            return new WhisperReadinessCheck(
                "microphone",
                "Microphone",
                WhisperReadinessState.Blocked,
                "Windows has not granted Soltex microphone access.",
                "Open Windows microphone privacy settings");
        }

        return inputs.MicrophoneSelected
            ? new WhisperReadinessCheck(
                "microphone",
                "Microphone",
                WhisperReadinessState.Ready,
                "An input device is selected and available.",
                nextAction: null)
            : new WhisperReadinessCheck(
                "microphone",
                "Microphone",
                WhisperReadinessState.NeedsSetup,
                "No input device is selected.",
                "Choose an input device");
    }

    private static WhisperReadinessCheck EvaluateShortcuts(WhisperReadinessInputs inputs)
    {
        if (inputs.ShortcutRegistrationError is { Length: > 0 } error)
        {
            return new WhisperReadinessCheck(
                "shortcuts",
                "Shortcuts",
                WhisperReadinessState.Blocked,
                WhisperRedaction.Sanitize(error, 160),
                "Choose a different chord");
        }

        return inputs.ShortcutsRegistered
            ? new WhisperReadinessCheck(
                "shortcuts",
                "Shortcuts",
                WhisperReadinessState.Ready,
                "Push-to-talk and hands-free chords are registered.",
                nextAction: null)
            : new WhisperReadinessCheck(
                "shortcuts",
                "Shortcuts",
                WhisperReadinessState.NeedsSetup,
                "No global shortcut is registered yet.",
                "Register shortcuts");
    }

    private static WhisperReadinessCheck EvaluateTranscriber(WhisperReadinessInputs inputs)
    {
        if (!inputs.TranscriberConfigured)
        {
            return new WhisperReadinessCheck(
                "transcriber",
                "Transcription",
                WhisperReadinessState.NeedsSetup,
                "No transcription provider is selected.",
                "Choose a provider");
        }

        return inputs.TranscriberCredentialAvailable
            ? new WhisperReadinessCheck(
                "transcriber",
                "Transcription",
                WhisperReadinessState.Ready,
                "A provider is selected and its credential is available.",
                nextAction: null)
            : new WhisperReadinessCheck(
                "transcriber",
                "Transcription",
                WhisperReadinessState.Blocked,
                "The selected provider has no stored credential.",
                "Add the provider credential");
    }

    private static WhisperReadinessCheck EvaluateTargetInspection(WhisperReadinessInputs inputs) =>
        inputs.TargetInspectionAvailable
            ? new WhisperReadinessCheck(
                "target-inspection",
                "Target access",
                WhisperReadinessState.Ready,
                "Whisper can identify the focused text control before inserting.",
                nextAction: null)
            : new WhisperReadinessCheck(
                "target-inspection",
                "Target access",
                WhisperReadinessState.NeedsSetup,
                "Whisper cannot inspect focused controls, so it will copy instead of insert.",
                "Review target access");

    private static WhisperReadinessCheck EvaluateAutoSend(WhisperReadinessInputs inputs)
    {
        if (!inputs.AutoSendEnabled)
        {
            return new WhisperReadinessCheck(
                "auto-send",
                "Auto-send",
                WhisperReadinessState.Ready,
                "Auto-send is off. Whisper inserts text and never presses Enter.",
                nextAction: null);
        }

        if (!inputs.AutoSendWarningAccepted)
        {
            return new WhisperReadinessCheck(
                "auto-send",
                "Auto-send",
                WhisperReadinessState.NeedsSetup,
                "Auto-send is on but the first-use warning has not been accepted, so Enter stays blocked.",
                "Review the auto-send warning");
        }

        return inputs.EnabledAutoSendProfileCount > 0
            ? new WhisperReadinessCheck(
                "auto-send",
                "Auto-send",
                WhisperReadinessState.Ready,
                $"Auto-send is allowed in {inputs.EnabledAutoSendProfileCount} approved application(s).",
                nextAction: null)
            : new WhisperReadinessCheck(
                "auto-send",
                "Auto-send",
                WhisperReadinessState.NeedsSetup,
                "Auto-send is on but no application is approved, so Enter stays blocked.",
                "Approve an application");
    }
}
