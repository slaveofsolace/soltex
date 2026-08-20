namespace Soltex.Whisper;

public sealed record WhisperSessionSnapshot(
    WhisperSessionState State,
    WhisperCaptureMode? Mode,
    DateTimeOffset? StartedAtUtc,
    string? LastError);

public sealed class WhisperSessionController
{
    private readonly object _sync = new();
    private WhisperSessionState _state;
    private WhisperCaptureMode? _mode;
    private DateTimeOffset? _startedAtUtc;
    private string? _lastError;

    public WhisperSessionController()
    {
        _state = WhisperSessionState.Idle;
    }

    public WhisperSessionSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new WhisperSessionSnapshot(_state, _mode, _startedAtUtc, _lastError);
        }
    }

    public void BeginListening(
        WhisperCaptureMode mode,
        DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            RequireState(WhisperSessionState.Idle);
            _mode = mode;
            _startedAtUtc = startedAtUtc;
            _lastError = null;
            _state = WhisperSessionState.Listening;
        }
    }

    public void MarkTranscribing()
    {
        lock (_sync)
        {
            RequireState(WhisperSessionState.Listening);
            _state = WhisperSessionState.Transcribing;
        }
    }

    public void MarkProcessing()
    {
        lock (_sync)
        {
            RequireState(WhisperSessionState.Transcribing);
            _state = WhisperSessionState.Processing;
        }
    }

    public void MarkDelivering()
    {
        lock (_sync)
        {
            RequireState(WhisperSessionState.Processing);
            _state = WhisperSessionState.Delivering;
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            if (_state is not (WhisperSessionState.Processing or WhisperSessionState.Delivering))
            {
                throw CreateTransitionException(
                    _state,
                    WhisperSessionState.Completed);
            }

            _state = WhisperSessionState.Completed;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (!IsActive(_state))
            {
                throw CreateTransitionException(
                    _state,
                    WhisperSessionState.Cancelled);
            }

            _state = WhisperSessionState.Cancelled;
        }
    }

    public void Fail(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        string normalized = error.Trim();
        if (normalized.Length > 256 || normalized.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(error),
                "Failure details must contain 1 to 256 printable characters.");
        }

        lock (_sync)
        {
            if (!IsActive(_state))
            {
                throw CreateTransitionException(
                    _state,
                    WhisperSessionState.Faulted);
            }

            _lastError = normalized;
            _state = WhisperSessionState.Faulted;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            if (_state is not (
                    WhisperSessionState.Completed or
                    WhisperSessionState.Cancelled or
                    WhisperSessionState.Faulted))
            {
                throw CreateTransitionException(
                    _state,
                    WhisperSessionState.Idle);
            }

            _state = WhisperSessionState.Idle;
            _mode = null;
            _startedAtUtc = null;
            _lastError = null;
        }
    }

    public WhisperDurationState GetHandsFreeDurationState(
        DateTimeOffset observedAtUtc)
    {
        lock (_sync)
        {
            if (_mode != WhisperCaptureMode.HandsFree ||
                _startedAtUtc is null ||
                !IsActive(_state))
            {
                return WhisperDurationState.Current;
            }

            TimeSpan elapsed = observedAtUtc - _startedAtUtc.Value;
            if (elapsed >= WhisperLimits.MaximumHandsFreeDuration)
            {
                return WhisperDurationState.Expired;
            }

            return elapsed >= WhisperLimits.HandsFreeWarningAt
                ? WhisperDurationState.Warning
                : WhisperDurationState.Current;
        }
    }

    private static bool IsActive(WhisperSessionState state)
    {
        return state is
            WhisperSessionState.Listening or
            WhisperSessionState.Transcribing or
            WhisperSessionState.Processing or
            WhisperSessionState.Delivering;
    }

    private void RequireState(WhisperSessionState expected)
    {
        if (_state != expected)
        {
            throw CreateTransitionException(_state, expected);
        }
    }

    private static InvalidOperationException CreateTransitionException(
        WhisperSessionState current,
        WhisperSessionState requested)
    {
        return new InvalidOperationException(
            $"Whisper cannot transition from {current} to {requested}.");
    }
}
