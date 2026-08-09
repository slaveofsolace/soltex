using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soltex.App;

internal static class RenderSmokeCapture
{
    internal const int PixelWidth = 1280;
    internal const int PixelHeight = 820;

    internal static void ConfigureWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;
        window.ShowActivated = false;
        window.MinWidth = PixelWidth;
        window.MaxWidth = PixelWidth;
        window.Width = PixelWidth;
        window.MinHeight = PixelHeight;
        window.MaxHeight = PixelHeight;
        window.Height = PixelHeight;
    }

    internal static RenderTargetBitmap Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.InvalidateMeasure();
        window.InvalidateArrange();
        window.InvalidateVisual();
        window.UpdateLayout();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);

        int actualWidth = Math.Max(1, checked((int)Math.Ceiling(window.ActualWidth)));
        int actualHeight = Math.Max(1, checked((int)Math.Ceiling(window.ActualHeight)));
        if (actualWidth != PixelWidth || actualHeight != PixelHeight)
        {
            throw new InvalidOperationException(
                $"Render-smoke window is {actualWidth}x{actualHeight}; " +
                $"expected {PixelWidth}x{PixelHeight}.");
        }

        RenderTargetBitmap warmup = new(
            PixelWidth,
            PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        warmup.Render(window);
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);

        RenderTargetBitmap bitmap = new(
            PixelWidth,
            PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }
}
