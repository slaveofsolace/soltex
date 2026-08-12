using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;

namespace Soltex.App;

public partial class MainWindow
{
    private bool _telemetryReconcileQueued;
    private bool _telemetryLifecycleClosing;

    internal bool IsPerformanceSamplingActive => _telemetryLoop.IsActive;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += TelemetryLifecycle_Loaded;
        StateChanged += TelemetryLifecycle_StateChanged;
        IsVisibleChanged += TelemetryLifecycle_IsVisibleChanged;
        HomePanel.IsVisibleChanged += TelemetryPanel_IsVisibleChanged;
        MonitoringPanel.IsVisibleChanged += TelemetryPanel_IsVisibleChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
        {
            _shutdownStarted = true;
            _telemetryLifecycleClosing = true;
            QueueTelemetryReconcile();
        }
    }

    private void TelemetryLifecycle_Loaded(object sender, RoutedEventArgs e) =>
        QueueTelemetryReconcile();

    private void TelemetryLifecycle_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            StopWorkspaceAnimations();
        }

        QueueTelemetryReconcile();
    }

    private void TelemetryLifecycle_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        QueueTelemetryReconcile();

    private void TelemetryPanel_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        QueueTelemetryReconcile();

    private void QueueTelemetryReconcile()
    {
        if (_telemetryReconcileQueued || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _telemetryReconcileQueued = true;
        _ = Dispatcher.InvokeAsync(
            ReconcileTelemetryFromDispatcherAsync,
            DispatcherPriority.ContextIdle);
    }

    private async void ReconcileTelemetryFromDispatcherAsync()
    {
        _telemetryReconcileQueued = false;
        try
        {
            await ReconcileTelemetryLoopAsync();
        }
        catch (OperationCanceledException)
        {
            // Window shutdown or a superseding lifecycle transition.
        }
    }

    private async Task ReconcileTelemetryLoopAsync()
    {
        bool shouldRun = TelemetryActivityPolicy.ShouldRun(
            IsLoaded,
            IsVisible,
            _telemetryLifecycleClosing,
            WindowState,
            HomePanel.IsVisible,
            MonitoringPanel.IsVisible);
        if (shouldRun)
        {
            await _telemetryLoop.StartAsync(RunTelemetryLoopAsync);
            return;
        }

        await _telemetryLoop.StopAsync();
    }
}

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The async gate has the MainWindow process lifetime and never exposes its optional OS wait handle; StopAsync owns every cancellation source and loop task.")]
internal sealed class TelemetryLoopOwner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private int _active;

    internal bool IsActive => Volatile.Read(ref _active) != 0;

    internal async Task StartAsync(Func<CancellationToken, Task> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        await _gate.WaitAsync();
        try
        {
            if (_cancellation is not null)
            {
                return;
            }

            CancellationTokenSource cancellation = new();
            try
            {
                Task loopTask = start(cancellation.Token);
                _cancellation = cancellation;
                _loopTask = loopTask;
                Volatile.Write(ref _active, 1);
            }
            catch
            {
                cancellation.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            CancellationTokenSource? cancellation = _cancellation;
            Task? loopTask = _loopTask;
            _cancellation = null;
            _loopTask = null;
            Volatile.Write(ref _active, 0);
            if (cancellation is null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
                if (loopTask is not null)
                {
                    await loopTask;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Expected when visibility or shutdown stops the owned loop.
            }
            finally
            {
                cancellation.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
