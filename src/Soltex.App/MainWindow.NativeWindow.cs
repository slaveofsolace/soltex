using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Soltex.NativeShell;

namespace Soltex.App;

public partial class MainWindow
{
    private const double NativeTitleBarHeight = 40d;
    private const double NativeCaptionFallbackWidth = 138d;

    private WindowsNativeWindowSession? _nativeWindowSession;
    private NativeWindowAttachment? _nativeWindowAttachment;
    private ResolvedAppearance _nativeResolvedAppearance = ResolvedAppearance.Dark;
    private bool _nativeHighContrastOverride;

    internal NativeWindowAttachment? NativeWindowAttachment => _nativeWindowAttachment;

    internal double NativeCaptionReserveDip => NativeCaptionRightInsetColumn.Width.Value;

    private void InitializeNativeWindowLifecycle()
    {
        SourceInitialized += Window_SourceInitialized;
        DpiChanged += Window_DpiChanged;
        NativeTitleBarDragRegion.SizeChanged += NativeTitleBarDragRegion_SizeChanged;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        nint windowHandle = new WindowInteropHelper(this).Handle;
        NativeWindowCapabilityInputs inputs = WindowsNativeWindowSession.ProbeCapabilities(
            _nativeHighContrastOverride || SystemParameters.HighContrast,
            SystemParameters.IsGlassEnabled);
        _nativeWindowAttachment = WindowsNativeWindowSession.TryAttach(
            windowHandle,
            inputs,
            UseDarkCaption(),
            out _nativeWindowSession);

        ApplyNativeTitleBarState(_nativeWindowAttachment);
        ApplyNativeBackdropSurface(_nativeWindowAttachment);
        QueueNativeDragRegionUpdate();
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateNativeCaptionInsets();
        QueueNativeDragRegionUpdate();
    }

    private void NativeTitleBarDragRegion_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        QueueNativeDragRegionUpdate();

    private void UpdateNativeWindowAppearance(
        ResolvedAppearance appearance,
        bool highContrastOverride)
    {
        _nativeResolvedAppearance = appearance;
        _nativeHighContrastOverride = highContrastOverride;

        if (_nativeWindowSession is null || _nativeWindowAttachment is null)
        {
            return;
        }

        NativeWindowCapabilityInputs inputs = WindowsNativeWindowSession.ProbeCapabilities(
            highContrastOverride || appearance == ResolvedAppearance.HighContrast,
            SystemParameters.IsGlassEnabled);
        NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(inputs);
        bool backdropApplied = _nativeWindowSession.TryUpdateBackdrop(
            decision.Backdrop,
            UseDarkCaption());
        if (!backdropApplied && decision.Backdrop == NativeWindowBackdropKind.Mica)
        {
            decision = decision with
            {
                Backdrop = NativeWindowBackdropKind.Solid,
                BackdropFallbackReason = NativeWindowFallbackReason.NativeCallFailed
            };
        }

        _nativeWindowAttachment = _nativeWindowAttachment with
        {
            Decision = decision,
            BackdropApplied = backdropApplied
        };
        ApplyNativeBackdropSurface(_nativeWindowAttachment);
    }

    private void ApplyNativeTitleBarState(NativeWindowAttachment attachment)
    {
        bool available = attachment.AppWindowAttached &&
                         attachment.Decision.UsesNativeTitleBar;
        NativeTitleBarRow.Height = available
            ? new GridLength(NativeTitleBarHeight)
            : new GridLength(0);
        NativeTitleBar.Visibility = available
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateNativeCaptionInsets();
    }

    private void ApplyNativeBackdropSurface(NativeWindowAttachment attachment)
    {
        string backdropBrush = attachment.BackdropApplied &&
                               attachment.Decision.Backdrop == NativeWindowBackdropKind.Mica
            ? "ShellBackdropBrush"
            : "CanvasBrush";
        SetResourceReference(BackgroundProperty, backdropBrush);
        ShellRoot.SetResourceReference(BackgroundProperty, backdropBrush);
    }

    private void UpdateNativeCaptionInsets()
    {
        if (_nativeWindowAttachment is not { AppWindowAttached: true } attachment)
        {
            NativeCaptionLeftInsetColumn.Width = new GridLength(0);
            NativeCaptionRightInsetColumn.Width = new GridLength(0);
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d;
        NativeCaptionLeftInsetColumn.Width = new GridLength(
            Math.Max(0, attachment.CaptionLeftInset / scale));
        NativeCaptionRightInsetColumn.Width = new GridLength(
            attachment.CaptionRightInset > 0
                ? attachment.CaptionRightInset / scale
                : NativeCaptionFallbackWidth);
    }

    private void QueueNativeDragRegionUpdate()
    {
        if (_nativeWindowSession is null ||
            NativeTitleBar.Visibility != Visibility.Visible ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateNativeDragRegion));
    }

    private void UpdateNativeDragRegion()
    {
        if (_nativeWindowSession is null ||
            NativeTitleBarDragRegion.ActualWidth <= 0 ||
            NativeTitleBarDragRegion.ActualHeight <= 0)
        {
            return;
        }

        Point origin;
        try
        {
            origin = NativeTitleBarDragRegion
                .TransformToAncestor(this)
                .Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        if (NativeWindowPolicy.TryCreateDragRectangle(
                origin.X,
                origin.Y,
                NativeTitleBarDragRegion.ActualWidth,
                NativeTitleBarDragRegion.ActualHeight,
                dpi.DpiScaleX,
                out NativePixelRect rectangle))
        {
            _nativeWindowSession.TrySetDragRectangles([rectangle]);
        }
    }

    private void UpdateNativeWorkspaceTitle(UIElement panel)
    {
        string workspaceTitle = panel == MonitoringPanel ? "Performance" :
            panel == ApplicationsPanel ? "Applications" :
            panel == DevicesPanel ? "Devices" :
            panel == MixerPanel ? "Audio" :
            panel == ClipsPanel ? "Capture" :
            panel == SecurityPanel ? "Security" :
            panel == RemotePanel ? "Remote Assist" :
            panel == WhisperPanel ? "Whisper" :
            panel == ActivityPanel ? "Activity" :
            panel == UpdatePanel ? "Updates" :
            panel == SettingsPanel ? "Settings" :
            "Overview";

        NativeWorkspaceTitle.Text = workspaceTitle;
        Title = $"Soltex — {workspaceTitle}";
    }

    private bool UseDarkCaption() =>
        !_nativeHighContrastOverride &&
        _nativeResolvedAppearance == ResolvedAppearance.Dark;

    private void DisposeNativeWindowLifecycle()
    {
        SourceInitialized -= Window_SourceInitialized;
        DpiChanged -= Window_DpiChanged;
        NativeTitleBarDragRegion.SizeChanged -= NativeTitleBarDragRegion_SizeChanged;
        _nativeWindowSession?.Dispose();
        _nativeWindowSession = null;
        _nativeWindowAttachment = null;
    }
}
