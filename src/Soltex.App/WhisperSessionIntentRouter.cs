using Soltex.Whisper;

namespace Soltex.App;

internal enum WhisperSessionHostAction
{
    None,
    StartSession,
    CompleteCapture,
    LockHandsFree,
    Cancel,
    OpenScratchpad,
    UseLastTranscript
}

internal sealed record WhisperSessionHostCommand(
    WhisperSessionHostAction Action,
    WhisperCaptureMode? Mode = null,
    bool HandsFreeLocked = false,
    WhisperShortcutIntent? LastTranscriptIntent = null);

/// <summary>
/// Pure host routing for shortcut intents. It decides only which already-authorized
/// session operation to invoke; insertion and Enter authorization remain in the
/// provider-neutral Whisper policies.
/// </summary>
internal static class WhisperSessionIntentRouter
{
    internal static WhisperSessionHostCommand Route(
        WhisperShortcutIntent intent,
        bool hasActiveSession,
        WhisperCaptureMode? activeMode) => intent switch
        {
            WhisperShortcutIntent.Cancel => new(WhisperSessionHostAction.Cancel),
            WhisperShortcutIntent.EndPushToTalk or WhisperShortcutIntent.EndCommandMode =>
                new(WhisperSessionHostAction.CompleteCapture),
            WhisperShortcutIntent.OpenScratchpad =>
                new(WhisperSessionHostAction.OpenScratchpad),
            WhisperShortcutIntent.BeginPushToTalk => new(
                WhisperSessionHostAction.StartSession,
                WhisperCaptureMode.PushToTalk),
            WhisperShortcutIntent.BeginCommandMode => new(
                WhisperSessionHostAction.StartSession,
                WhisperCaptureMode.Command),
            WhisperShortcutIntent.ToggleHandsFree when
                hasActiveSession && activeMode == WhisperCaptureMode.HandsFree =>
                new(WhisperSessionHostAction.CompleteCapture),
            WhisperShortcutIntent.ToggleHandsFree => new(
                WhisperSessionHostAction.StartSession,
                WhisperCaptureMode.HandsFree),
            WhisperShortcutIntent.LockHandsFree when
                hasActiveSession && activeMode == WhisperCaptureMode.HandsFree =>
                new(WhisperSessionHostAction.LockHandsFree,
                    WhisperCaptureMode.HandsFree,
                    HandsFreeLocked: true),
            WhisperShortcutIntent.LockHandsFree => new(
                WhisperSessionHostAction.StartSession,
                WhisperCaptureMode.HandsFree,
                HandsFreeLocked: true),
            WhisperShortcutIntent.PasteLastTranscript or
                WhisperShortcutIntent.CopyLastTranscript or
                WhisperShortcutIntent.SubmitLastTranscript => new(
                    WhisperSessionHostAction.UseLastTranscript,
                    LastTranscriptIntent: intent),
            _ => new(WhisperSessionHostAction.None)
        };
}
