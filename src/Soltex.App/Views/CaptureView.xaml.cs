using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Capture;

namespace Soltex.App.Views;

public sealed class CaptureScreenshotRequestedEventArgs(bool includePointer) : EventArgs
{
    public bool IncludePointer { get; } = includePointer;
}

public partial class CaptureView : UserControl
{
    private bool _screenshotAvailable;
    private bool _busy;

    public CaptureView()
    {
        InitializeComponent();
    }

    public event EventHandler<CaptureScreenshotRequestedEventArgs>? ScreenshotRequested;

    public event EventHandler? OpenFolderRequested;

    internal void UpdateCapabilities(
        CaptureCapabilitySnapshot snapshot,
        string storageLabel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CaptureCapability? screenshot = snapshot.Capabilities.FirstOrDefault(item =>
            string.Equals(item.Id, "screenshot", StringComparison.Ordinal));
        _screenshotAvailable = screenshot?.Available == true;
        CaptureStatusPillText.Text = _screenshotAvailable ? "READY" : "UNAVAILABLE";
        CaptureStatusPillText.Foreground = (Brush)FindResource(
            _screenshotAvailable ? "SignalBrush" : "WarningBrush");
        CaptureActionTitle.Text = _screenshotAvailable
            ? "Choose a window or display"
            : "Capture unavailable";
        CaptureActionDetail.Text = screenshot?.Detail ?? "Windows screen capture is unavailable.";
        CaptureActionDetail.Visibility = _screenshotAvailable
            ? Visibility.Collapsed
            : Visibility.Visible;
        CaptureStorageText.Text =
            $"Saved to {storageLabel}. Soltex does not upload it or add file details to Activity.";
        CaptureFooterText.Text = snapshot.Limitation.Length > 0
            ? snapshot.Limitation
            : _screenshotAvailable
                ? "Ready for a display or window"
                : "Windows capture is unavailable";
        RefreshActionState();
    }

    internal void SetBusy(bool busy)
    {
        _busy = busy;
        TakeScreenshotButton.Content = busy ? "Choosing…" : "Take screenshot";
        IncludePointerCheckBox.IsEnabled = !busy;
        if (busy)
        {
            CaptureStatusPillText.Text = "CHOOSING";
            CaptureStatusPillText.Foreground = (Brush)FindResource("AccentFocusBrush");
            CaptureActionTitle.Text = "Select a window or display";
            CaptureActionDetail.Text = "Continue in the Windows picker.";
            CaptureActionDetail.Visibility = Visibility.Visible;
            CaptureFooterText.Text = "Waiting for your selection";
        }
        RefreshActionState();
    }

    internal void ShowResult(CaptureScreenshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ClipManifest manifest = result.Manifest;
        CaptureStatusPillText.Text = "SAVED";
        CaptureStatusPillText.Foreground = (Brush)FindResource("SignalBrush");
        CaptureActionTitle.Text = "Take another screenshot";
        CaptureActionDetail.Text = "Windows will ask what to capture.";
        CaptureActionDetail.Visibility = Visibility.Visible;
        LastCaptureTitle.Text = "Screenshot saved";
        LastCaptureDetail.Text =
            $"{manifest.Width:N0} × {manifest.Height:N0} · {DescribeBytes(manifest.FileBytes)} · PNG";
        LastCapturePanel.Visibility = Visibility.Visible;
        CaptureFooterText.Text = "Saved locally";
    }

    internal void ShowCancelled()
    {
        CaptureStatusPillText.Text = "READY";
        CaptureStatusPillText.Foreground = (Brush)FindResource("SignalBrush");
        CaptureActionTitle.Text = "Choose a window or display";
        CaptureActionDetail.Text = "Nothing was captured.";
        CaptureActionDetail.Visibility = Visibility.Visible;
        CaptureFooterText.Text = "Ready for a display or window";
    }

    internal void ShowFailure(string detail)
    {
        CaptureStatusPillText.Text = "NEEDS ATTENTION";
        CaptureStatusPillText.Foreground = (Brush)FindResource("WarningBrush");
        CaptureActionTitle.Text = "Screenshot not saved";
        CaptureActionDetail.Text = string.IsNullOrWhiteSpace(detail)
            ? "Windows could not complete the screenshot."
            : detail;
        CaptureActionDetail.Visibility = Visibility.Visible;
        CaptureFooterText.Text = "No file was kept";
    }

    private void RefreshActionState()
    {
        TakeScreenshotButton.IsEnabled = _screenshotAvailable && !_busy;
    }

    private void TakeScreenshotButton_Click(object sender, RoutedEventArgs e) =>
        ScreenshotRequested?.Invoke(
            this,
            new CaptureScreenshotRequestedEventArgs(IncludePointerCheckBox.IsChecked == true));

    private void OpenCaptureFolderButton_Click(object sender, RoutedEventArgs e) =>
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private static string DescribeBytes(long bytes) => bytes switch
    {
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} MB",
        >= 1_024 => $"{bytes / 1_024d:F0} KB",
        _ => $"{bytes} B"
    };
}
