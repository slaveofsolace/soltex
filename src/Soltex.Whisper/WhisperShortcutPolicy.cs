namespace Soltex.Whisper;

public sealed record WhisperShortcutValidationResult(bool IsValid, string? Error)
{
    public static WhisperShortcutValidationResult Valid { get; } = new(true, null);

    public static WhisperShortcutValidationResult Reject(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new WhisperShortcutValidationResult(false, error);
    }
}

/// <summary>
/// Platform-neutral safety checks that run before a Windows hook can be installed.
/// The policy rejects chords Windows owns and ambiguous prefix combinations that
/// could start the wrong action while the user is still pressing a longer chord.
/// </summary>
public static class WhisperShortcutRegistrationPolicy
{
    private static readonly HashSet<string> Modifiers =
        new(StringComparer.OrdinalIgnoreCase) { "Ctrl", "Win", "Shift", "Alt" };

    private static readonly HashSet<string> ReservedChords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Alt+F4",
            "Alt+Tab",
            "Ctrl+Esc",
            "Ctrl+Shift+Esc",
            "Ctrl+Alt+DELETE"
        };

    public static WhisperShortcutValidationResult Validate(WhisperShortcutSet shortcutSet)
    {
        ArgumentNullException.ThrowIfNull(shortcutSet);

        foreach (WhisperShortcutBinding binding in shortcutSet.Bindings)
        {
            if (binding.Keys.Contains("Win", StringComparer.OrdinalIgnoreCase))
            {
                return WhisperShortcutValidationResult.Reject(
                    $"{binding.DisplayText} uses the Windows key, which is reserved for operating-system shortcuts.");
            }

            if (ReservedChords.Contains(binding.DisplayText))
            {
                return WhisperShortcutValidationResult.Reject(
                    $"{binding.DisplayText} is reserved by Windows and cannot be assigned.");
            }

            bool onlyModifiers = binding.Keys.All(Modifiers.Contains);
            bool allowedSingleKey = binding.Keys.Count == 1 &&
                ((binding.Action == WhisperShortcutAction.Cancel &&
                  string.Equals(binding.Keys[0], "Esc", StringComparison.OrdinalIgnoreCase)) ||
                 binding.Keys[0] is "Mouse 4" or "Mouse 5");
            if (onlyModifiers || (binding.Keys.Count == 1 && !allowedSingleKey))
            {
                return WhisperShortcutValidationResult.Reject(
                    $"{binding.DisplayText} is too broad; use a modifier plus a non-modifier key.");
            }
        }

        WhisperShortcutBinding[] bindings = shortcutSet.Bindings.ToArray();
        for (int left = 0; left < bindings.Length; left++)
        {
            for (int right = left + 1; right < bindings.Length; right++)
            {
                if (IsStrictSubset(bindings[left], bindings[right]) ||
                    IsStrictSubset(bindings[right], bindings[left]))
                {
                    return WhisperShortcutValidationResult.Reject(
                        $"{bindings[left].DisplayText} and {bindings[right].DisplayText} overlap; neither shortcut may be a prefix of another.");
                }
            }
        }

        return WhisperShortcutValidationResult.Valid;
    }

    private static bool IsStrictSubset(
        WhisperShortcutBinding candidate,
        WhisperShortcutBinding other)
    {
        return candidate.Keys.Count < other.Keys.Count &&
            candidate.Keys.All(key => other.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Converts content-free key transitions into session intents. It owns gesture
/// semantics so the Windows hook remains a transport adapter only.
/// </summary>
public sealed class WhisperShortcutGestureInterpreter
{
    public static TimeSpan DefaultDoubleTapWindow { get; } = TimeSpan.FromMilliseconds(400);

    private readonly TimeSpan _doubleTapWindow;
    private TimeSpan? _lastHandsFreePress;

    public WhisperShortcutGestureInterpreter(TimeSpan? doubleTapWindow = null)
    {
        _doubleTapWindow = doubleTapWindow ?? DefaultDoubleTapWindow;
        if (_doubleTapWindow <= TimeSpan.Zero || _doubleTapWindow > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(doubleTapWindow),
                "The hands-free double-tap window must be greater than zero and no more than one second.");
        }
    }

    public WhisperShortcutIntent? Observe(WhisperShortcutSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.MonotonicTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signal),
                "Shortcut timestamps cannot be negative.");
        }

        return signal.Action switch
        {
            WhisperShortcutAction.PushToTalk => signal.Transition == WhisperShortcutTransition.Pressed
                ? WhisperShortcutIntent.BeginPushToTalk
                : WhisperShortcutIntent.EndPushToTalk,
            WhisperShortcutAction.CommandMode => signal.Transition == WhisperShortcutTransition.Pressed
                ? WhisperShortcutIntent.BeginCommandMode
                : WhisperShortcutIntent.EndCommandMode,
            WhisperShortcutAction.HandsFree => InterpretHandsFree(signal),
            WhisperShortcutAction.PasteLastTranscript => OnPress(
                signal,
                WhisperShortcutIntent.PasteLastTranscript),
            WhisperShortcutAction.CopyLastTranscript => OnPress(
                signal,
                WhisperShortcutIntent.CopyLastTranscript),
            WhisperShortcutAction.Cancel => OnPress(signal, WhisperShortcutIntent.Cancel),
            WhisperShortcutAction.OpenScratchpad => OnPress(
                signal,
                WhisperShortcutIntent.OpenScratchpad),
            WhisperShortcutAction.SubmitLastTranscript => OnPress(
                signal,
                WhisperShortcutIntent.SubmitLastTranscript),
            _ => null
        };
    }

    private WhisperShortcutIntent? InterpretHandsFree(WhisperShortcutSignal signal)
    {
        if (signal.Transition != WhisperShortcutTransition.Pressed)
        {
            return null;
        }

        WhisperShortcutIntent intent = _lastHandsFreePress is TimeSpan previous &&
            signal.MonotonicTime >= previous &&
            signal.MonotonicTime - previous <= _doubleTapWindow
                ? WhisperShortcutIntent.LockHandsFree
                : WhisperShortcutIntent.ToggleHandsFree;
        _lastHandsFreePress = intent == WhisperShortcutIntent.LockHandsFree
            ? null
            : signal.MonotonicTime;
        return intent;
    }

    private static WhisperShortcutIntent? OnPress(
        WhisperShortcutSignal signal,
        WhisperShortcutIntent intent) =>
        signal.Transition == WhisperShortcutTransition.Pressed ? intent : null;
}
