using System.Diagnostics;
using System.Threading.Channels;

namespace WaveSlate.Security;

public sealed class ProtectionMonitor : IDisposable, IAsyncDisposable
{
    private readonly IProtectionHealthSource _source;
    private readonly WindowsSecurityChangeMonitor? _changeMonitor;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _failureRetryInterval;
    private readonly TimeSpan _maximumBackoff;
    private readonly Channel<bool> _refreshSignals;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private Task? _worker;
    private DefenderHealthSnapshot? _lastKnownGood;
    private ProtectionMonitorUpdate? _lastUpdate;
    private DateTimeOffset? _lastSuccessfulCheckAtUtc;
    private int _consecutiveFailures;
    private int _disposed;

    public ProtectionMonitor(
        IProtectionHealthSource source,
        WindowsSecurityChangeMonitor? changeMonitor = null,
        TimeSpan? pollInterval = null,
        TimeSpan? failureRetryInterval = null,
        TimeSpan? maximumBackoff = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _changeMonitor = changeMonitor;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(1);
        _failureRetryInterval = failureRetryInterval ?? TimeSpan.FromSeconds(5);
        _maximumBackoff = maximumBackoff ?? TimeSpan.FromMinutes(5);

        if (_pollInterval <= TimeSpan.Zero ||
            _failureRetryInterval <= TimeSpan.Zero ||
            _maximumBackoff < _failureRetryInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Monitoring intervals must be positive and maximum backoff cannot be shorter than retry delay.");
        }

        _refreshSignals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        if (_changeMonitor is not null)
        {
            _changeMonitor.Changed += OnWindowsSecurityChanged;
        }
    }

    public event Action<ProtectionMonitorUpdate>? Updated;

    public ProtectionMonitorUpdate? LastUpdate => Volatile.Read(ref _lastUpdate);

    public bool ChangeNotificationsAvailable => _changeMonitor?.IsRegistered == true;

    public string? ChangeNotificationError => _changeMonitor?.Error;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_lifecycleGate)
        {
            _worker ??= Task.Run(RunAsync);
        }
    }

    public bool RequestRefresh()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _refreshSignals.Writer.TryWrite(true);
    }

    public async Task<ProtectionMonitorUpdate> RefreshOnceAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ProtectionMonitorUpdate update;
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            DefenderHealthSnapshot observed;
            try
            {
                observed = await _source.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                observed = UnavailableHealth(
                    $"Protection health source failed ({exception.GetType().Name}).");
            }

            stopwatch.Stop();
            if (observed.ObservationSucceeded)
            {
                int recoveredFailures = _consecutiveFailures;
                _consecutiveFailures = 0;
                _lastKnownGood = observed;
                _lastSuccessfulCheckAtUtc = observed.CheckedAtUtc;
                ProtectionMonitorState state = recoveredFailures > 0
                    ? ProtectionMonitorState.Recovered
                    : observed.StatusQuerySucceeded
                        ? ProtectionMonitorState.Current
                        : ProtectionMonitorState.ProviderManaged;
                string detail = state == ProtectionMonitorState.Recovered
                    ? $"Protection monitoring recovered after {recoveredFailures} failed observation(s)."
                    : state == ProtectionMonitorState.ProviderManaged
                        ? "Windows Security Center supplied aggregate provider health; Defender-specific details are unavailable."
                        : observed.Summary;
                update = new ProtectionMonitorUpdate(
                    observed,
                    _lastKnownGood,
                    state,
                    ConsecutiveFailures: 0,
                    _lastSuccessfulCheckAtUtc,
                    stopwatch.Elapsed,
                    _pollInterval,
                    detail);
            }
            else
            {
                _consecutiveFailures++;
                TimeSpan retryDelay = CalculateBackoff(_consecutiveFailures);
                update = new ProtectionMonitorUpdate(
                    observed,
                    _lastKnownGood,
                    ProtectionMonitorState.Degraded,
                    _consecutiveFailures,
                    _lastSuccessfulCheckAtUtc,
                    stopwatch.Elapsed,
                    retryDelay,
                    observed.Error ?? "No supported Windows protection source returned a current observation.");
            }

            Volatile.Write(ref _lastUpdate, update);
        }
        finally
        {
            _refreshGate.Release();
        }

        NotifyUpdated(update);
        return update;
    }

    private async Task RunAsync()
    {
        try
        {
            if (LastUpdate is null)
            {
                await RefreshOnceAsync(_stopping.Token).ConfigureAwait(false);
            }

            while (!_stopping.IsCancellationRequested)
            {
                TimeSpan delay = LastUpdate?.NextRefreshIn ?? _pollInterval;
                using CancellationTokenSource waitCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                waitCancellation.CancelAfter(delay);

                bool refreshRequested;
                try
                {
                    refreshRequested = await _refreshSignals.Reader
                        .WaitToReadAsync(waitCancellation.Token)
                        .ConfigureAwait(false);
                    if (!refreshRequested)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (
                    waitCancellation.IsCancellationRequested &&
                    !_stopping.IsCancellationRequested)
                {
                    refreshRequested = false;
                }

                if (refreshRequested)
                {
                    while (_refreshSignals.Reader.TryRead(out _))
                    {
                    }
                }

                await RefreshOnceAsync(_stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
    }

    private void OnWindowsSecurityChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Volatile.Read(ref _disposed) == 0)
        {
            _refreshSignals.Writer.TryWrite(true);
        }
    }

    private void NotifyUpdated(ProtectionMonitorUpdate update)
    {
        Delegate[] handlers = Updated?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<ProtectionMonitorUpdate>)handler)(update);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    private TimeSpan CalculateBackoff(int failures)
    {
        double multiplier = Math.Pow(2, Math.Min(failures - 1, 10));
        double ticks = Math.Min(_failureRetryInterval.Ticks * multiplier, _maximumBackoff.Ticks);
        return TimeSpan.FromTicks(checked((long)ticks));
    }

    private static DefenderHealthSnapshot UnavailableHealth(string error) => new(
        DateTimeOffset.UtcNow,
        WindowsSecurityHealth.Unknown,
        StatusQuerySucceeded: false,
        AMRunningMode: null,
        AMServiceEnabled: false,
        AntivirusEnabled: false,
        RealTimeProtectionEnabled: false,
        BehaviorMonitorEnabled: false,
        IoavProtectionEnabled: false,
        NetworkInspectionEnabled: false,
        TamperProtected: false,
        CloudProtectionEnabled: false,
        SignaturesOutOfDate: false,
        AntivirusSignatureVersion: null,
        AntivirusSignatureUpdatedAt: null,
        QuickScanAgeDays: null,
        FullScanAgeDays: null,
        Error: error);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_changeMonitor is not null)
        {
            _changeMonitor.Changed -= OnWindowsSecurityChanged;
        }

        _refreshSignals.Writer.TryComplete();
        _stopping.Cancel();
        Task? worker;
        lock (_lifecycleGate)
        {
            worker = _worker;
        }

        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _changeMonitor?.Dispose();
        _refreshGate.Dispose();
        _stopping.Dispose();
        Updated = null;
        GC.SuppressFinalize(this);
    }
}
