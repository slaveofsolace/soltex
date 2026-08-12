using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soltex.App;

public partial class App : Application
{
    public App()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (string.Equals(argument, "--render-smoke", StringComparison.OrdinalIgnoreCase))
            {
                // This must happen before InitializeComponent loads any font-backed
                // resources; OnStartup is too late for deterministic text capture.
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                break;
            }
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window = new();
        MainWindow = window;

        if (e.Args is ["--render-smoke", var outputPath])
        {
            RenderSmokeSnapshot(window, outputPath);
            return;
        }

        if (e.Args is ["--render-smoke", var panelOutputPath, "--panel", var panelName])
        {
            if (!window.TrySelectRenderSmokePanel(panelName))
            {
                throw new ArgumentException(
                    "The render-smoke panel must be one of: home, monitoring, monitoring-details, applications, settings, devices, " +
                    "security, security-activity, remote, activity, update, mixer, mixer-more, clips.");
            }

            RenderSmokeSnapshot(window, panelOutputPath);
            return;
        }

        window.Show();
    }

    private void RenderSmokeSnapshot(MainWindow window, string outputPath)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("The render-smoke output path must include a directory.", nameof(outputPath));
        }

        Directory.CreateDirectory(outputDirectory);
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        RenderSmokeCapture.ConfigureWindow(window);
        window.Left = -32_000;
        window.Top = -32_000;
        window.Show();

        DispatcherTimer timer = new(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            int exitCode = 0;
            try
            {
                window.InvalidateMeasure();
                window.InvalidateArrange();
                window.InvalidateVisual();
                window.UpdateLayout();
                window.PrepareRenderSmokeCapture();
                window.UpdateLayout();
                RenderTargetBitmap bitmap = RenderSmokeCapture.Capture(window);
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = new(fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                exitCode = 1;
                File.WriteAllText(fullOutputPath + ".error.txt", exception.ToString());
            }
            finally
            {
                Environment.ExitCode = exitCode;
                window.Close();
                Shutdown(exitCode);
            }
        };
        timer.Start();
    }
}
