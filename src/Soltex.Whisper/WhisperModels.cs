using System.Collections.ObjectModel;

namespace Soltex.Whisper;

public static class WhisperLimits
{
    public const int MaximumTranscriptCharacters = 200_000;
    public const int MaximumReadbackCharacters = MaximumTranscriptCharacters * 2;
    public const int MaximumSnippetCount = 256;
    public const int MaximumSnippetCueCharacters = 80;
    public const int MaximumSnippetContentCharacters = 64_000;
    public const int MaximumProcessNameCharacters = 128;
    public const int MaximumHistoryEntries = 100;
    public const int MaximumShortcutKeys = 3;
    public const int MaximumShortcutsPerAction = 4;

    public static TimeSpan HandsFreeWarningAt { get; } = TimeSpan.FromMinutes(19);

    public static TimeSpan MaximumHandsFreeDuration { get; } = TimeSpan.FromMinutes(20);
}

public enum WhisperCaptureMode
{
    PushToTalk,
    HandsFree,
    Command
}

public enum WhisperSessionState
{
    Idle,
    Listening,
    Transcribing,
    Processing,
    Delivering,
    Completed,
    Cancelled,
    Faulted
}

public enum WhisperDurationState
{
    Current,
    Warning,
    Expired
}

public enum WhisperTargetKind
{
    Unknown,
    PlainText,
    RichText,
    Terminal,
    Browser,
    Editor
}

public enum WhisperTargetIntegrityLevel
{
    Unknown,
    Untrusted,
    Low,
    Medium,
    High,
    System,
    Protected
}

/// <summary>
/// Content-free editing capabilities reported by a target adapter. These flags
/// describe pattern support only; they never contain a field value, selection,
/// caption, or surrounding text.
/// </summary>
public sealed record WhisperTargetCapabilities(
    bool SupportsValuePattern,
    bool SupportsTextPattern,
    bool SupportsTextPattern2,
    bool SupportsSelection,
    bool SupportsCaret)
{
    public static WhisperTargetCapabilities None { get; } = new(
        SupportsValuePattern: false,
        SupportsTextPattern: false,
        SupportsTextPattern2: false,
        SupportsSelection: false,
        SupportsCaret: false);
}

public enum WhisperDeliveryKind
{
    None,
    InsertText,
    CopyText,
    InsertAndSubmit,
    SubmitOnly
}

public enum WhisperSubmitOrigin
{
    None,
    TerminalPhrase,
    DedicatedShortcut
}

public enum WhisperShortcutAction
{
    PushToTalk,
    HandsFree,
    CommandMode,
    PasteLastTranscript,
    CopyLastTranscript,
    Cancel,
    OpenScratchpad,
    SubmitLastTranscript
}

public enum WhisperShortcutTransition
{
    Pressed,
    Released
}

/// <summary>
/// A content-free transition from a configured shortcut. The timestamp is monotonic
/// elapsed time supplied by the platform adapter; it is not a wall-clock observation.
/// </summary>
public sealed record WhisperShortcutSignal(
    WhisperShortcutAction Action,
    WhisperShortcutTransition Transition,
    TimeSpan MonotonicTime);

public enum WhisperShortcutIntent
{
    BeginPushToTalk,
    EndPushToTalk,
    ToggleHandsFree,
    LockHandsFree,
    BeginCommandMode,
    EndCommandMode,
    PasteLastTranscript,
    CopyLastTranscript,
    Cancel,
    OpenScratchpad,
    SubmitLastTranscript
}

public sealed record WhisperTextOptions(
    bool SmartFormatting,
    bool Backtrack,
    bool ExpandSnippets,
    bool DetectTerminalSubmit)
{
    public static WhisperTextOptions Default { get; } = new(
        SmartFormatting: true,
        Backtrack: true,
        ExpandSnippets: true,
        DetectTerminalSubmit: true);
}

public sealed class WhisperSnippet
{
    public WhisperSnippet(string cue, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cue);
        ArgumentNullException.ThrowIfNull(content);

        string normalizedCue = cue.Trim();
        if (normalizedCue.Length > WhisperLimits.MaximumSnippetCueCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cue),
                $"Snippet cues cannot exceed {WhisperLimits.MaximumSnippetCueCharacters} characters.");
        }

        if (normalizedCue.Any(char.IsControl))
        {
            throw new ArgumentException("Snippet cues cannot contain control characters.", nameof(cue));
        }

        if (content.Length > WhisperLimits.MaximumSnippetContentCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"Snippet content cannot exceed {WhisperLimits.MaximumSnippetContentCharacters} characters.");
        }

        Cue = normalizedCue;
        Content = content;
    }

    public string Cue { get; }

    public string Content { get; }
}

public sealed class WhisperAppProfile
{
    public WhisperAppProfile(
        string processName,
        bool autoSendAllowed,
        bool terminalAutoSendAllowed = false,
        bool clipboardFallbackAllowed = true,
        bool contextFormattingAllowed = true,
        string styleName = "Default")
    {
        ProcessName = NormalizeProcessName(processName);
        ArgumentException.ThrowIfNullOrWhiteSpace(styleName);

        string normalizedStyle = styleName.Trim();
        if (normalizedStyle.Length > 64 || normalizedStyle.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(styleName),
                "Style names must contain 1 to 64 printable characters.");
        }

        AutoSendAllowed = autoSendAllowed;
        TerminalAutoSendAllowed = terminalAutoSendAllowed;
        ClipboardFallbackAllowed = clipboardFallbackAllowed;
        ContextFormattingAllowed = contextFormattingAllowed;
        StyleName = normalizedStyle;
    }

    public string ProcessName { get; }

    public bool AutoSendAllowed { get; }

    public bool TerminalAutoSendAllowed { get; }

    public bool ClipboardFallbackAllowed { get; }

    public bool ContextFormattingAllowed { get; }

    public string StyleName { get; }

    public bool MatchesProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(
            ProcessName,
            NormalizeProcessName(processName),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeProcessName(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        string normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (normalized.Length is 0 or > WhisperLimits.MaximumProcessNameCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processName),
                $"Process names must contain 1 to {WhisperLimits.MaximumProcessNameCharacters} characters.");
        }

        if (normalized.Any(character =>
                char.IsControl(character) ||
                character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new ArgumentException(
                "Process names must be names only and cannot contain paths or control characters.",
                nameof(processName));
        }

        return normalized;
    }
}

public sealed class WhisperTargetContext
{
    public WhisperTargetContext(
        string processName,
        WhisperTargetKind kind,
        bool isKnown,
        bool isEditable,
        bool isPassword,
        bool isReadOnly,
        bool isElevated,
        WhisperTargetCapabilities? capabilities = null,
        WhisperTargetIntegrityLevel? integrityLevel = null)
    {
        ProcessName = string.IsNullOrWhiteSpace(processName)
            ? "unknown"
            : WhisperAppProfile.NormalizeProcessName(processName);
        Kind = kind;
        IsKnown = isKnown;
        IsEditable = isEditable;
        IsPassword = isPassword;
        IsReadOnly = isReadOnly;
        IntegrityLevel = integrityLevel ?? (isElevated
            ? WhisperTargetIntegrityLevel.High
            : WhisperTargetIntegrityLevel.Medium);
        IsElevated = isElevated || IntegrityLevel is
            WhisperTargetIntegrityLevel.High or
            WhisperTargetIntegrityLevel.System or
            WhisperTargetIntegrityLevel.Protected;
        Capabilities = capabilities ?? WhisperTargetCapabilities.None;
    }

    public string ProcessName { get; }

    public WhisperTargetKind Kind { get; }

    public bool IsKnown { get; }

    public bool IsEditable { get; }

    public bool IsPassword { get; }

    public bool IsReadOnly { get; }

    public bool IsElevated { get; }

    public WhisperTargetIntegrityLevel IntegrityLevel { get; }

    public WhisperTargetCapabilities Capabilities { get; }

    public static WhisperTargetContext Unknown { get; } = new(
        "unknown",
        WhisperTargetKind.Unknown,
        isKnown: false,
        isEditable: false,
        isPassword: false,
        isReadOnly: false,
        isElevated: false,
        capabilities: WhisperTargetCapabilities.None,
        integrityLevel: WhisperTargetIntegrityLevel.Unknown);
}

public sealed class WhisperPipelineResult
{
    public WhisperPipelineResult(
        string text,
        bool submitRequested,
        WhisperSubmitOrigin submitOrigin,
        IEnumerable<string> appliedOperations)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(appliedOperations);

        if (text.Length > WhisperLimits.MaximumTranscriptCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Processed text cannot exceed {WhisperLimits.MaximumTranscriptCharacters} characters.");
        }

        if (submitRequested && submitOrigin == WhisperSubmitOrigin.None)
        {
            throw new ArgumentException(
                "A requested submission must identify its origin.",
                nameof(submitOrigin));
        }

        string[] operations = appliedOperations.ToArray();
        if (operations.Any(operation =>
                string.IsNullOrWhiteSpace(operation) ||
                operation.Length > 64 ||
                operation.Any(char.IsControl)))
        {
            throw new ArgumentException(
                "Applied operation names must contain 1 to 64 printable characters.",
                nameof(appliedOperations));
        }

        Text = text;
        SubmitRequested = submitRequested;
        SubmitOrigin = submitRequested ? submitOrigin : WhisperSubmitOrigin.None;
        AppliedOperations = Array.AsReadOnly(operations);
    }

    public string Text { get; }

    public bool SubmitRequested { get; }

    public WhisperSubmitOrigin SubmitOrigin { get; }

    public ReadOnlyCollection<string> AppliedOperations { get; }
}

public sealed record WhisperDeliveryDecision(
    WhisperDeliveryKind Kind,
    string Text,
    WhisperSubmitOrigin SubmitOrigin,
    bool RestoreClipboard,
    string Reason);

public sealed record WhisperHistoryEntry(
    DateTimeOffset CreatedAtUtc,
    string ProcessName,
    WhisperDeliveryKind DeliveryKind,
    string Text);
