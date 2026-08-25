using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using Soltex.App.Views;
using Soltex.Capture;

namespace Soltex.App;

public partial class MainWindow
{
    private readonly CaptureSessionController _captureSession = new();
    private CancellationTokenSource? _captureCancellation;
    private Task _captureOperationDrained = Task.CompletedTask;
    private string _captureOutputRoot = string.Empty;

    private void InitializeCaptureWorkspace()
    {
        _captureOutputRoot = ResolveCaptureOutputRoot();
        CaptureCapabilitySnapshot capabilities = _renderSmokeMode
            ? new CaptureCapabilitySnapshot(
                DateTimeOffset.UtcNow,
                [
                    new CaptureCapability(
                        "screenshot",
                        Available: true,
                        RequiresConsent: true,
                        "Windows will ask you to choose a display or window."),
                    new CaptureCapability("recording", false, true, "Video recording is not enabled yet."),
                    new CaptureCapability("replay", false, true, "Instant replay is unavailable.")
                ])
            : WindowsGraphicsCaptureScreenshotService.Probe();
        CapturePanel.UpdateCapabilities(capabilities, DescribeCaptureOutputRoot());
        CapturePanel.ScreenshotRequested += CapturePanel_ScreenshotRequested;
        CapturePanel.OpenFolderRequested += CapturePanel_OpenFolderRequested;
    }

    private void CapturePanel_ScreenshotRequested(
        object? sender,
        CaptureScreenshotRequestedEventArgs e)
    {
        if (!_captureOperationDrained.IsCompleted || _shutdownStarted)
        {
            return;
        }

        _captureCancellation?.Dispose();
        _captureCancellation = new CancellationTokenSource();
        _captureOperationDrained = RunScreenshotAsync(
            e.IncludePointer,
            _captureCancellation.Token);
    }

    private async Task RunScreenshotAsync(
        bool includePointer,
        CancellationToken cancellationToken)
    {
        try
        {
            CapturePanel.SetBusy(true);
            _captureSession.BeginSelection();
            nint owner = new WindowInteropHelper(this).Handle;
            CaptureScreenshotResult? result =
                await WindowsGraphicsCaptureScreenshotService.CaptureAsync(
                    new CaptureScreenshotRequest(
                        owner,
                        _captureOutputRoot,
                        includePointer),
                    cancellationToken);
            if (result is null)
            {
                _captureSession.Cancel("Source selection was cancelled.");
                CapturePanel.ShowCancelled();
                return;
            }

            _captureSession.CompleteScreenshot(
                result.SourceLabel,
                result.Manifest.CreatedAtUtc);
            CapturePanel.ShowResult(result);
            AddActivity("Screenshot saved.", "Capture");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_captureSession.Snapshot.Phase is not
                (CaptureSessionPhase.Completed or CaptureSessionPhase.Faulted))
            {
                _captureSession.Cancel();
            }
            if (!_shutdownStarted)
            {
                CapturePanel.ShowCancelled();
            }
        }
        catch (CaptureScreenshotException exception)
        {
            if (_captureSession.Snapshot.Phase is not
                (CaptureSessionPhase.Completed or CaptureSessionPhase.Faulted))
            {
                _captureSession.Fail(exception.Kind, exception.Message);
            }
            CapturePanel.ShowFailure(exception.Message);
            AddActivity("Screenshot could not be saved.", "Capture");
        }
        finally
        {
            if (!_shutdownStarted)
            {
                CapturePanel.SetBusy(false);
            }
        }
    }

    private void CapturePanel_OpenFolderRequested(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_captureOutputRoot);
            string explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            ProcessStartInfo startInfo = new(explorer)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(_captureOutputRoot);
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CapturePanel.ShowFailure("The screenshot folder could not be opened.");
        }
    }

    private string ResolveCaptureOutputRoot()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrWhiteSpace(pictures)
            ? Path.Combine(_runtime.DataRoot, "capture", "clips")
            : Path.Combine(pictures, "Soltex");
    }

    private string DescribeCaptureOutputRoot()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return !string.IsNullOrWhiteSpace(pictures) &&
               _captureOutputRoot.StartsWith(pictures, StringComparison.OrdinalIgnoreCase)
            ? "Pictures\\Soltex"
            : "the Soltex data folder";
    }
}
