using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soltex.App;

internal static class RenderSmokeCapture
{
    internal const int PixelWidth = 1280;
    internal const int PixelHeight = 820;

    internal static void ConfigureWindow(
        Window window,
        RenderSmokeViewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        RenderSmokeViewport resolved = viewport ??
            RuntimeLaunchPolicy.ResolveRenderViewport(RenderSmokeProfile.Standard100);

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;
        window.ShowActivated = false;
        window.MinWidth = resolved.LogicalWidth;
        window.MaxWidth = resolved.LogicalWidth;
        window.Width = resolved.LogicalWidth;
        window.MinHeight = resolved.LogicalHeight;
        window.MaxHeight = resolved.LogicalHeight;
        window.Height = resolved.LogicalHeight;
    }

    internal static RenderTargetBitmap Capture(
        Window window,
        RenderSmokeViewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        RenderSmokeViewport resolved = viewport ??
            RuntimeLaunchPolicy.ResolveRenderViewport(RenderSmokeProfile.Standard100);

        window.InvalidateMeasure();
        window.InvalidateArrange();
        window.InvalidateVisual();
        window.UpdateLayout();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);

        int actualWidth = Math.Max(1, checked((int)Math.Ceiling(window.ActualWidth)));
        int actualHeight = Math.Max(1, checked((int)Math.Ceiling(window.ActualHeight)));
        if (actualWidth != resolved.LogicalWidth || actualHeight != resolved.LogicalHeight)
        {
            throw new InvalidOperationException(
                $"Render-smoke window is {actualWidth}x{actualHeight}; " +
                $"expected {resolved.LogicalWidth}x{resolved.LogicalHeight}.");
        }

        RenderTargetBitmap warmup = new(
            resolved.PixelWidth,
            resolved.PixelHeight,
            resolved.Dpi,
            resolved.Dpi,
            PixelFormats.Pbgra32);
        warmup.Render(window);
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);

        RenderTargetBitmap bitmap = new(
            resolved.PixelWidth,
            resolved.PixelHeight,
            resolved.Dpi,
            resolved.Dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    internal static RenderSmokeViewport CreateOverlayViewport(
        Window window,
        int scalePercent)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (scalePercent is not (100 or 150 or 200))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scalePercent),
                "Overlay evidence density must be 100, 150, or 200 percent.");
        }

        window.InvalidateMeasure();
        window.InvalidateArrange();
        window.UpdateLayout();
        int logicalWidth = Math.Max(1, checked((int)Math.Ceiling(window.ActualWidth)));
        int logicalHeight = Math.Max(1, checked((int)Math.Ceiling(window.ActualHeight)));
        window.SizeToContent = SizeToContent.Manual;
        window.MinWidth = logicalWidth;
        window.MaxWidth = logicalWidth;
        window.Width = logicalWidth;
        window.MinHeight = logicalHeight;
        window.MaxHeight = logicalHeight;
        window.Height = logicalHeight;
        return new RenderSmokeViewport(logicalWidth, logicalHeight, scalePercent);
    }
}
