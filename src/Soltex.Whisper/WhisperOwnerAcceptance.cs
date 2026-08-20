using System.Collections.ObjectModel;
using System.Globalization;

namespace Soltex.Whisper;

/// <summary>
/// Owner-observed checks that cannot be honestly replaced by deterministic CI.
/// The tracker is session-only and records categories and bounded timings, never
/// keys, audio, transcript text, target text, process names, or local paths.
/// </summary>
public enum WhisperOwnerCheckKind
{
    PhysicalShortcut,
    VerifiedSpokenInsertion,
    EscapeCancellation,
    MicrophoneReconnect,
    LiveDpiTransition,
    KeyboardAndScreenReader
}

public enum WhisperOwnerCheckState
{
    Pending,
    Waiting,
    Observed,
    Passed,
    NeedsAttention
}

public sealed record WhisperOwnerAcceptanceCheck(
    WhisperOwnerCheckKind Kind,
    WhisperOwnerCheckState State,
    string Title,
    string Detail,
    double? MeasurementMilliseconds = null);

public sealed record WhisperOwnerAcceptanceSnapshot(
    IReadOnlyList<WhisperOwnerAcceptanceCheck> Checks,
    WhisperOwnerCheckKind? ActiveCheck,
    WhisperOwnerCheckKind? NextCheck)
{
    public int PassedCount => Checks.Count(check => check.State == WhisperOwnerCheckState.Passed);

    public bool IsComplete => PassedCount == Checks.Count;
}

/// <summary>
/// Tracks one explicitly armed owner check at a time. Observation methods accept
/// only content-free policy results so sensitive session data cannot enter this state.
/// </summary>
public sealed class WhisperOwnerAcceptanceTracker
{
    private static readonly WhisperOwnerCheckKind[] OrderedKinds =
    [
        WhisperOwnerCheckKind.PhysicalShortcut,
        WhisperOwnerCheckKind.VerifiedSpokenInsertion,
        WhisperOwnerCheckKind.EscapeCancellation,
        WhisperOwnerCheckKind.MicrophoneReconnect,
        WhisperOwnerCheckKind.LiveDpiTransition,
        WhisperOwnerCheckKind.KeyboardAndScreenReader
    ];

    private readonly object _gate = new();
    private readonly Dictionary<WhisperOwnerCheckKind, MutableCheck> _checks =
        OrderedKinds.ToDictionary(kind => kind, CreatePending);
    private WhisperOwnerCheckKind? _activeCheck;
    private TimeSpan? _physicalShortcutAt;
    private bool _escapeObserved;
    private bool _microphoneDisconnected;

    public WhisperOwnerAcceptanceSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot BeginNext(bool microphoneAvailable)
    {
        lock (_gate)
        {
            WhisperOwnerCheckKind? next = OrderedKinds.FirstOrDefault(kind =>
                _checks[kind].State != WhisperOwnerCheckState.Passed);
            if (OrderedKinds.All(kind =>
                    _checks[kind].State == WhisperOwnerCheckState.Passed))
            {
                return CreateSnapshotCore();
            }

            return BeginCore(next!.Value, microphoneAvailable);
        }
    }

    public WhisperOwnerAcceptanceSnapshot Begin(
        WhisperOwnerCheckKind kind,
        bool microphoneAvailable)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            return BeginCore(kind, microphoneAvailable);
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveShortcut(
        WhisperShortcutSignal signal,
        WhisperShortcutIntent intent)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent));
        }

        lock (_gate)
        {
            if (_activeCheck == WhisperOwnerCheckKind.PhysicalShortcut &&
                signal.Action == WhisperShortcutAction.PushToTalk &&
                signal.Transition == WhisperShortcutTransition.Pressed &&
                intent == WhisperShortcutIntent.BeginPushToTalk)
            {
                _physicalShortcutAt = signal.MonotonicTime;
                SetObserved(
                    WhisperOwnerCheckKind.PhysicalShortcut,
                    "Physical push-to-talk arrived. Waiting for the listening surface.");
            }
            else if (_activeCheck == WhisperOwnerCheckKind.EscapeCancellation &&
                     signal.Action == WhisperShortcutAction.Cancel &&
                     signal.Transition == WhisperShortcutTransition.Pressed &&
                     intent == WhisperShortcutIntent.Cancel)
            {
                _escapeObserved = true;
                SetObserved(
                    WhisperOwnerCheckKind.EscapeCancellation,
                    "Physical Escape arrived. Waiting for owned session cancellation.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveSessionState(
        WhisperSessionState state,
        TimeSpan monotonicNow)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        lock (_gate)
        {
            if (_activeCheck == WhisperOwnerCheckKind.PhysicalShortcut &&
                state == WhisperSessionState.Listening &&
                _physicalShortcutAt is TimeSpan shortcutAt)
            {
                TimeSpan elapsed = monotonicNow - shortcutAt;
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(10))
                {
                    SetNeedsAttention(
                        WhisperOwnerCheckKind.PhysicalShortcut,
                        "The shortcut arrived, but listening latency was outside the ten-second evidence bound.");
                }
                else
                {
                    SetPassed(
                        WhisperOwnerCheckKind.PhysicalShortcut,
                        $"Physical shortcut reached listening in {elapsed.TotalMilliseconds:F1} ms.",
                        elapsed.TotalMilliseconds);
                }
            }
            else if (_activeCheck == WhisperOwnerCheckKind.EscapeCancellation &&
                     state == WhisperSessionState.Cancelled)
            {
                if (_escapeObserved)
                {
                    SetPassed(
                        WhisperOwnerCheckKind.EscapeCancellation,
                        "Physical Escape cancelled and drained the owned session.");
                }
                else
                {
                    SetNeedsAttention(
                        WhisperOwnerCheckKind.EscapeCancellation,
                        "The session cancelled without an observed physical Escape shortcut.");
                }
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveSpokenInsertion(
        bool transcriptProduced,
        WhisperTextDeliveryResult delivery,
        WhisperInsertionVerification verification)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(verification);
        lock (_gate)
        {
            if (_activeCheck != WhisperOwnerCheckKind.VerifiedSpokenInsertion)
            {
                return CreateSnapshotCore();
            }

            if (!transcriptProduced)
            {
                SetNeedsAttention(
                    WhisperOwnerCheckKind.VerifiedSpokenInsertion,
                    "The owner session completed without a usable local transcript.");
            }
            else if (!delivery.MutationDispatched ||
                     delivery.Method is WhisperInsertionMethod.None or
                         WhisperInsertionMethod.ClipboardCopy)
            {
                SetNeedsAttention(
                    WhisperOwnerCheckKind.VerifiedSpokenInsertion,
                    "The owner session did not insert into a confirmed editable target.");
            }
            else if (!verification.Verified)
            {
                SetNeedsAttention(
                    WhisperOwnerCheckKind.VerifiedSpokenInsertion,
                    "Insertion was attempted, but target-owned read-back did not verify it.");
            }
            else
            {
                SetPassed(
                    WhisperOwnerCheckKind.VerifiedSpokenInsertion,
                    $"Target-owned read-back verified {Describe(delivery.Method)} insertion.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveSessionFailure()
    {
        lock (_gate)
        {
            if (_activeCheck is WhisperOwnerCheckKind.PhysicalShortcut or
                WhisperOwnerCheckKind.VerifiedSpokenInsertion or
                WhisperOwnerCheckKind.EscapeCancellation)
            {
                SetNeedsAttention(
                    _activeCheck.Value,
                    "The owner check ended safely before its required observation completed.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveMicrophoneAvailability(bool available)
    {
        lock (_gate)
        {
            if (_activeCheck != WhisperOwnerCheckKind.MicrophoneReconnect)
            {
                return CreateSnapshotCore();
            }

            if (!available)
            {
                _microphoneDisconnected = true;
                SetObserved(
                    WhisperOwnerCheckKind.MicrophoneReconnect,
                    "The selected microphone disappeared. Reconnect it, then check devices again.");
            }
            else if (_microphoneDisconnected)
            {
                SetPassed(
                    WhisperOwnerCheckKind.MicrophoneReconnect,
                    "The selected microphone disappeared and returned through bounded discovery.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ObserveDpiTransition(
        double previousScale,
        double currentScale)
    {
        if (!double.IsFinite(previousScale) || previousScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousScale));
        }

        if (!double.IsFinite(currentScale) || currentScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentScale));
        }

        lock (_gate)
        {
            if (_activeCheck == WhisperOwnerCheckKind.LiveDpiTransition &&
                Math.Abs(currentScale - previousScale) >= 0.01)
            {
                string percent = (currentScale * 100).ToString("F0", CultureInfo.InvariantCulture);
                SetPassed(
                    WhisperOwnerCheckKind.LiveDpiTransition,
                    $"Windows delivered a live DPI transition to {percent}% scale.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot ConfirmKeyboardAndScreenReader()
    {
        lock (_gate)
        {
            if (_activeCheck == WhisperOwnerCheckKind.KeyboardAndScreenReader)
            {
                SetPassed(
                    WhisperOwnerCheckKind.KeyboardAndScreenReader,
                    "Owner confirmed keyboard-only navigation and a screen-reader walkthrough.");
            }

            return CreateSnapshotCore();
        }
    }

    public WhisperOwnerAcceptanceSnapshot Reset()
    {
        lock (_gate)
        {
            foreach (WhisperOwnerCheckKind kind in OrderedKinds)
            {
                _checks[kind] = CreatePending(kind);
            }

            ClearActiveState();
            return CreateSnapshotCore();
        }
    }

    private WhisperOwnerAcceptanceSnapshot BeginCore(
        WhisperOwnerCheckKind kind,
        bool microphoneAvailable)
    {
        if (_activeCheck is not null)
        {
            throw new InvalidOperationException(
                "Finish or reset the active owner check before starting another.");
        }

        if (_checks[kind].State == WhisperOwnerCheckState.Passed)
        {
            throw new InvalidOperationException("That owner check has already passed.");
        }

        ClearObservationState();
        _activeCheck = kind;
        MutableCheck check = _checks[kind];
        check.State = WhisperOwnerCheckState.Waiting;
        check.Detail = WaitingDetail(kind);
        check.MeasurementMilliseconds = null;
        if (kind == WhisperOwnerCheckKind.MicrophoneReconnect && !microphoneAvailable)
        {
            SetNeedsAttention(
                kind,
                "Reconnect checking needs an available selected microphone before it starts.");
        }

        return CreateSnapshotCore();
    }

    private WhisperOwnerAcceptanceSnapshot CreateSnapshotCore()
    {
        WhisperOwnerAcceptanceCheck[] checks = OrderedKinds
            .Select(kind => _checks[kind].Snapshot(kind))
            .ToArray();
        WhisperOwnerCheckKind? next = checks
            .FirstOrDefault(check => check.State != WhisperOwnerCheckState.Passed)
            ?.Kind;
        return new WhisperOwnerAcceptanceSnapshot(
            new ReadOnlyCollection<WhisperOwnerAcceptanceCheck>(checks),
            _activeCheck,
            next);
    }

    private void SetObserved(WhisperOwnerCheckKind kind, string detail)
    {
        MutableCheck check = _checks[kind];
        check.State = WhisperOwnerCheckState.Observed;
        check.Detail = detail;
    }

    private void SetPassed(
        WhisperOwnerCheckKind kind,
        string detail,
        double? measurementMilliseconds = null)
    {
        MutableCheck check = _checks[kind];
        check.State = WhisperOwnerCheckState.Passed;
        check.Detail = detail;
        check.MeasurementMilliseconds = measurementMilliseconds;
        ClearActiveState();
    }

    private void SetNeedsAttention(WhisperOwnerCheckKind kind, string detail)
    {
        MutableCheck check = _checks[kind];
        check.State = WhisperOwnerCheckState.NeedsAttention;
        check.Detail = detail;
        check.MeasurementMilliseconds = null;
        ClearActiveState();
    }

    private void ClearActiveState()
    {
        _activeCheck = null;
        ClearObservationState();
    }

    private void ClearObservationState()
    {
        _physicalShortcutAt = null;
        _escapeObserved = false;
        _microphoneDisconnected = false;
    }

    private static MutableCheck CreatePending(WhisperOwnerCheckKind kind) => new(
        WhisperOwnerCheckState.Pending,
        PendingDetail(kind));

    private static string Title(WhisperOwnerCheckKind kind) => kind switch
    {
        WhisperOwnerCheckKind.PhysicalShortcut => "Physical shortcut",
        WhisperOwnerCheckKind.VerifiedSpokenInsertion => "Spoken insertion",
        WhisperOwnerCheckKind.EscapeCancellation => "Escape cancellation",
        WhisperOwnerCheckKind.MicrophoneReconnect => "Microphone reconnect",
        WhisperOwnerCheckKind.LiveDpiTransition => "Live display scale",
        WhisperOwnerCheckKind.KeyboardAndScreenReader => "Accessibility check",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string PendingDetail(WhisperOwnerCheckKind kind) => kind switch
    {
        WhisperOwnerCheckKind.PhysicalShortcut =>
            "Confirms a real push-to-talk chord and measures shortcut-to-listening latency.",
        WhisperOwnerCheckKind.VerifiedSpokenInsertion =>
            "Confirms local speech, insertion, and target-owned read-back without retaining text.",
        WhisperOwnerCheckKind.EscapeCancellation =>
            "Confirms physical Escape cancels and drains an active owner session.",
        WhisperOwnerCheckKind.MicrophoneReconnect =>
            "Confirms the selected input disappears and returns through bounded discovery.",
        WhisperOwnerCheckKind.LiveDpiTransition =>
            "Confirms Windows delivers a real per-monitor display-scale transition.",
        WhisperOwnerCheckKind.KeyboardAndScreenReader =>
            "Records an explicit owner walkthrough; automated checks cannot substitute for it.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string WaitingDetail(WhisperOwnerCheckKind kind) => kind switch
    {
        WhisperOwnerCheckKind.PhysicalShortcut =>
            "Use the configured push-to-talk chord once, then release it.",
        WhisperOwnerCheckKind.VerifiedSpokenInsertion =>
            "Focus an owner-controlled editable sample, dictate, and use its approved verified-submit path.",
        WhisperOwnerCheckKind.EscapeCancellation =>
            "Start dictation, then press physical Escape before capture completes.",
        WhisperOwnerCheckKind.MicrophoneReconnect =>
            "Disconnect the selected microphone, check devices, reconnect it, then check again.",
        WhisperOwnerCheckKind.LiveDpiTransition =>
            "Move Soltex to a monitor with a different Windows scale while this check is armed.",
        WhisperOwnerCheckKind.KeyboardAndScreenReader =>
            "Navigate Whisper by keyboard with a screen reader, then explicitly mark the walkthrough.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Describe(WhisperInsertionMethod method) => method switch
    {
        WhisperInsertionMethod.AutomationValue => "automation value",
        WhisperInsertionMethod.AutomationTextRange => "automation text-range",
        WhisperInsertionMethod.ClipboardPaste => "owned clipboard-paste",
        _ => "confirmed"
    };

    private sealed class MutableCheck(
        WhisperOwnerCheckState state,
        string detail)
    {
        internal WhisperOwnerCheckState State { get; set; } = state;

        internal string Detail { get; set; } = detail;

        internal double? MeasurementMilliseconds { get; set; }

        internal WhisperOwnerAcceptanceCheck Snapshot(WhisperOwnerCheckKind kind) => new(
            kind,
            State,
            Title(kind),
            Detail,
            MeasurementMilliseconds);
    }
}
