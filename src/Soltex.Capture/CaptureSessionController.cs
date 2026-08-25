namespace Soltex.Capture;

public sealed record CaptureSessionState(
    CaptureSessionPhase Phase,
    Guid SessionId,
    string SourceLabel,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    CaptureFailureKind FailureKind,
    string Detail)
{
    public static CaptureSessionState Idle { get; } =
        new(
            CaptureSessionPhase.Idle,
            Guid.Empty,
            string.Empty,
            null,
            null,
            CaptureFailureKind.None,
            string.Empty);

    public bool RequiresVisibleIndicator =>
        Phase is CaptureSessionPhase.Recording or CaptureSessionPhase.Paused or CaptureSessionPhase.Stopping;
}

public sealed class CaptureSessionController
{
    private readonly object _gate = new();
    private CaptureSessionState _state = CaptureSessionState.Idle;

    public CaptureSessionState Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public CaptureSessionState BeginSelection()
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Idle, CaptureSessionPhase.Completed, CaptureSessionPhase.Faulted);
            _state = new CaptureSessionState(
                CaptureSessionPhase.Selecting,
                Guid.NewGuid(),
                string.Empty,
                null,
                null,
                CaptureFailureKind.None,
                "Choose a display, window, or region.");
            return _state;
        }
    }

    public CaptureSessionState SourceSelected(string sourceLabel)
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Selecting);
            string label = CaptureCapabilitySnapshot.NormalizeText(sourceLabel, 96);
            if (label.Length == 0)
            {
                throw new ArgumentException("A source label is required.", nameof(sourceLabel));
            }

            _state = _state with
            {
                Phase = CaptureSessionPhase.Ready,
                SourceLabel = label,
                Detail = "Ready to capture."
            };
            return _state;
        }
    }

    public CaptureSessionState Start(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Ready);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Recording,
                StartedAtUtc = nowUtc,
                CompletedAtUtc = null,
                Detail = "Capture is active."
            };
            return _state;
        }
    }

    public CaptureSessionState CompleteScreenshot(
        string sourceLabel,
        DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Selecting);
            string label = CaptureCapabilitySnapshot.NormalizeText(sourceLabel, 96);
            if (label.Length == 0)
            {
                throw new ArgumentException("A source label is required.", nameof(sourceLabel));
            }

            _state = _state with
            {
                Phase = CaptureSessionPhase.Completed,
                SourceLabel = label,
                StartedAtUtc = nowUtc,
                CompletedAtUtc = nowUtc,
                FailureKind = CaptureFailureKind.None,
                Detail = "Screenshot saved."
            };
            return _state;
        }
    }

    public CaptureSessionState Pause()
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Recording);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Paused,
                Detail = "Capture is paused."
            };
            return _state;
        }
    }

    public CaptureSessionState Resume()
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Paused);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Recording,
                Detail = "Capture is active."
            };
            return _state;
        }
    }

    public CaptureSessionState BeginStopping()
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Recording, CaptureSessionPhase.Paused);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Stopping,
                Detail = "Finishing the capture."
            };
            return _state;
        }
    }

    public CaptureSessionState Complete(DateTimeOffset nowUtc, string detail = "Capture saved.")
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Stopping);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Completed,
                CompletedAtUtc = nowUtc,
                FailureKind = CaptureFailureKind.None,
                Detail = CaptureCapabilitySnapshot.NormalizeText(detail, 240)
            };
            return _state;
        }
    }

    public CaptureSessionState Cancel(string detail = "Capture cancelled.")
    {
        lock (_gate)
        {
            Require(
                CaptureSessionPhase.Selecting,
                CaptureSessionPhase.Ready,
                CaptureSessionPhase.Recording,
                CaptureSessionPhase.Paused,
                CaptureSessionPhase.Stopping);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Faulted,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FailureKind = CaptureFailureKind.Cancelled,
                Detail = CaptureCapabilitySnapshot.NormalizeText(detail, 240)
            };
            return _state;
        }
    }

    public CaptureSessionState Fail(CaptureFailureKind kind, string detail)
    {
        if (kind is CaptureFailureKind.None or CaptureFailureKind.Cancelled)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            Require(
                CaptureSessionPhase.Selecting,
                CaptureSessionPhase.Ready,
                CaptureSessionPhase.Recording,
                CaptureSessionPhase.Paused,
                CaptureSessionPhase.Stopping);
            _state = _state with
            {
                Phase = CaptureSessionPhase.Faulted,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FailureKind = kind,
                Detail = CaptureCapabilitySnapshot.NormalizeText(detail, 240)
            };
            return _state;
        }
    }

    public CaptureSessionState Reset()
    {
        lock (_gate)
        {
            Require(CaptureSessionPhase.Completed, CaptureSessionPhase.Faulted);
            _state = CaptureSessionState.Idle;
            return _state;
        }
    }

    private void Require(params CaptureSessionPhase[] phases)
    {
        if (!phases.Contains(_state.Phase))
        {
            throw new InvalidOperationException(
                $"Capture cannot move from {_state.Phase} to the requested state.");
        }
    }
}
