using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using Soltex.Benchmarks;
using Soltex.Monitoring;

namespace Soltex.App;

public partial class MainWindow
{
    private CancellationTokenSource? _benchmarkCancellation;
    private Task _benchmarkOperationDrained = Task.CompletedTask;

    private async void MonitoringPanel_BenchmarkRunRequested(object? sender, EventArgs e)
    {
        if (_benchmarkCancellation is not null ||
            !BenchmarkActivityPolicy.ShouldContinue(
                IsLoaded,
                IsVisible,
                _telemetryLifecycleClosing,
                WindowState,
                MonitoringPanel.IsVisible,
                MonitoringPanel.IsBenchmarkVisible))
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> drained = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _benchmarkCancellation = cancellation;
        _benchmarkOperationDrained = drained.Task;
        MonitoringPanel.ShowBenchmarkRunning(
            "Preparing three short local tests.");
        try
        {
            await _telemetryLoop.StopAsync();
            double? baselineCpu = null;
            try
            {
                SystemTelemetrySnapshot baseline = await SystemTelemetryProvider.CaptureAsync(
                    TimeSpan.FromMilliseconds(300),
                    cancellation.Token);
                baselineCpu = baseline.CpuPercent;
            }
            catch (Exception exception) when (IsExpectedBenchmarkBaselineFailure(exception))
            {
                baselineCpu = null;
            }

            BenchmarkResult result = await BenchmarkRunner.RunAsync(
                BenchmarkProfile.Quick,
                Path.Combine(_runtime.DataRoot, "benchmark-scratch"),
                baselineCpu,
                cancellation.Token);
            string persistenceDetail;
            try
            {
                _benchmarkResultStore.Save(result);
                persistenceDetail = BenchmarkResultStore.Describe(result);
            }
            catch (Exception exception) when (IsExpectedBenchmarkFailure(exception))
            {
                persistenceDetail = "The measured result could not be saved locally.";
            }
            MonitoringPanel.UpdateBenchmarkResult(result, persistenceDetail);
            AddActivity(
                "Quick local benchmark completed with named CPU, memory, and temporary-storage measurements.",
                "Performance");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                MonitoringPanel.ShowBenchmarkCancelled();
                AddActivity("Quick local benchmark cancelled; temporary storage was cleaned.", "Performance");
            }
        }
        catch (Exception exception) when (IsExpectedBenchmarkFailure(exception))
        {
            if (!_shutdownStarted)
            {
                MonitoringPanel.ShowBenchmarkFailed(
                    "The benchmark could not complete. Any temporary scratch file was removed and no score was retained.");
                AddActivity("Quick local benchmark could not complete; no score was retained.", "Performance");
            }
        }
        finally
        {
            if (ReferenceEquals(_benchmarkCancellation, cancellation))
            {
                _benchmarkCancellation = null;
            }

            cancellation.Dispose();
            drained.TrySetResult(true);
            QueueTelemetryReconcile();
        }
    }

    private void MonitoringPanel_BenchmarkCancelRequested(object? sender, EventArgs e) =>
        _benchmarkCancellation?.Cancel();

    private void MonitoringPanel_BenchmarkClearRequested(object? sender, EventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Clear the saved local benchmark result? This does not change Windows or delete other Soltex activity.",
            "Clear benchmark result",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _benchmarkResultStore.Clear();
            MonitoringPanel.UpdateBenchmarkLoad(new BenchmarkResultLoad(
                null,
                false,
                "No saved benchmark result."));
            AddActivity("Saved local benchmark result cleared.", "Performance");
        }
        catch (Exception exception) when (IsExpectedBenchmarkFailure(exception))
        {
            MonitoringPanel.ShowBenchmarkFailed(
                "The saved benchmark result could not be cleared. No benchmark was run.");
        }
    }

    private void MonitoringPanel_BenchmarkModeChanged(object? sender, EventArgs e)
    {
        CancelBenchmarkIfInactive();
        QueueTelemetryReconcile();
    }

    private void CancelBenchmarkIfInactive()
    {
        if (_benchmarkCancellation is not null &&
            !BenchmarkActivityPolicy.ShouldContinue(
                IsLoaded,
                IsVisible,
                _telemetryLifecycleClosing,
                WindowState,
                MonitoringPanel.IsVisible,
                MonitoringPanel.IsBenchmarkVisible))
        {
            _benchmarkCancellation.Cancel();
        }
    }

    private static bool IsExpectedBenchmarkBaselineFailure(Exception exception) =>
        IsExpectedTelemetryFailure(exception) || exception is OperationCanceledException;

    private static bool IsExpectedBenchmarkFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            CryptographicException or InvalidDataException or InvalidOperationException or
            ArgumentException;
}
