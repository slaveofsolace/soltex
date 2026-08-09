using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Soltex.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim _telemetryLifecycleGate = new(1, 1);
    private bool _telemetryReconcileQueued;
    private bool _telemetryLifecycleClosing;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += TelemetryLifecycle_Loaded;
        StateChanged += TelemetryLifecycle_StateChanged;
        HomePanel.IsVisibleChanged += TelemetryPanel_IsVisibleChanged;
        MonitoringPanel.IsVisibleChanged += TelemetryPanel_IsVisibleChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _telemetryLifecycleClosing = true;
        base.OnClosing(e);
    }

    private void TelemetryLifecycle_Loaded(object sender, RoutedEventArgs e) =>
        QueueTelemetryReconcile();

    private void TelemetryLifecycle_StateChanged(object? sender, EventArgs e) =>
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
        await _telemetryLifecycleGate.WaitAsync();
        try
        {
            bool shouldRun = TelemetryActivityPolicy.ShouldRun(
                IsLoaded,
                _telemetryLifecycleClosing,
                WindowState,
                HomePanel.IsVisible,
                MonitoringPanel.IsVisible);
            if (shouldRun)
            {
                if (_telemetryCancellation is null)
                {
                    _telemetryCancellation = new CancellationTokenSource();
                    _telemetryLoopTask = RunTelemetryLoopAsync(_telemetryCancellation.Token);
                }

                return;
            }

            CancellationTokenSource? cancellation = _telemetryCancellation;
            Task? loopTask = _telemetryLoopTask;
            _telemetryCancellation = null;
            _telemetryLoopTask = null;
            if (cancellation is null)
            {
                return;
            }

            cancellation.Cancel();
            if (loopTask is not null)
            {
                try
                {
                    await loopTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when a live workspace is hidden or minimized.
                }
            }

            cancellation.Dispose();
        }
        finally
        {
            _telemetryLifecycleGate.Release();
        }
    }
}
