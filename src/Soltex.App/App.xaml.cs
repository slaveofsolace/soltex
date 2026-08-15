using System.IO;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soltex.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; OnExit disposes notification-area resources.")]
public partial class App : Application
{
    private static readonly JsonSerializerOptions RuntimeReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private MainWindow? _ownedMainWindow;
    private NotificationAreaController? _notificationArea;
    private bool _explicitExitRequested;

    public App()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (RuntimeLaunchPolicy.UsesSoftwareRendering(arguments))
        {
            // This must happen before InitializeComponent loads any font-backed
            // resources; OnStartup is too late for deterministic text capture.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window = new();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (RuntimeLaunchPolicy.IsRuntimeProbe(e.Args))
        {
            RunRuntimeProbe(
                window,
                Environment.GetEnvironmentVariable("SOLTEX_RUNTIME_REPORT_PATH"),
                Environment.GetEnvironmentVariable("SOLTEX_SOURCE_HEAD_SHA"),
                Environment.GetEnvironmentVariable("SOLTEX_TESTED_COMMIT_SHA"));
            return;
        }

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
                    "The render-smoke panel must be one of: home, monitoring, monitoring-details, monitoring-benchmark, applications, applications-services, settings, devices, " +
                    "security, security-activity, remote, whisper, activity, update, mixer, mixer-devices, mixer-more, command-palette, clips.");
            }

            RenderSmokeSnapshot(window, panelOutputPath);
            return;
        }

        if (RuntimeLaunchPolicy.HasControlledRuntimeOption(e.Args))
        {
            throw new ArgumentException(
                "The controlled runtime arguments are incomplete, duplicated, or conflicting.");
        }

        StartNormalRuntime(window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notificationArea?.Dispose();
        _notificationArea = null;
        base.OnExit(e);
    }

    private void StartNormalRuntime(MainWindow window)
    {
        _ownedMainWindow = window;
        window.Closing += MainWindow_Closing;
        window.CloseBehaviorChanged += MainWindow_CloseBehaviorChanged;
        window.ShutdownCompleted += MainWindow_ShutdownCompleted;

        try
        {
            nint windowHandle = new WindowInteropHelper(window).EnsureHandle();
            _notificationArea = new NotificationAreaController(windowHandle);
            _notificationArea.OpenRequested += NotificationArea_OpenRequested;
            _notificationArea.ExitRequested += NotificationArea_ExitRequested;
            _notificationArea.AvailabilityChanged += NotificationArea_AvailabilityChanged;
        }
        catch (Exception exception) when (exception is ExternalException or
                                           InvalidOperationException or
                                           TypeInitializationException)
        {
            _notificationArea?.Dispose();
            _notificationArea = null;
        }

        window.SetNotificationAreaAvailability(_notificationArea?.IsAvailable == true);
        SyncNotificationAreaVisibility();
        window.Show();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_ownedMainWindow is null)
        {
            return;
        }

        bool hide = BackgroundRuntimePolicy.ShouldHideOnClose(
            renderSmokeMode: false,
            _explicitExitRequested,
            _ownedMainWindow.KeepsRunningInNotificationArea
                ? CloseBehavior.NotificationArea
                : CloseBehavior.Exit,
            _notificationArea?.IsAvailable == true);
        if (hide)
        {
            e.Cancel = true;
            _ownedMainWindow.Hide();
            SyncNotificationAreaVisibility();
            _notificationArea?.ShowBackgroundNotice();
            return;
        }

        if (_notificationArea is not null)
        {
            _notificationArea.SetVisible(false);
        }
    }

    private void MainWindow_CloseBehaviorChanged(object? sender, EventArgs e) =>
        SyncNotificationAreaVisibility();

    private void NotificationArea_AvailabilityChanged(object? sender, EventArgs e)
    {
        if (_ownedMainWindow is null || _notificationArea?.IsAvailable == true)
        {
            return;
        }

        _ownedMainWindow.SetNotificationAreaAvailability(available: false);
        if (!_ownedMainWindow.IsVisible)
        {
            _ownedMainWindow.Show();
            _ownedMainWindow.Activate();
        }
    }

    private void MainWindow_ShutdownCompleted(
        object? sender,
        ShutdownCompletedEventArgs e)
    {
        _notificationArea?.Dispose();
        _notificationArea = null;
        Shutdown(e.ResourcesDisposed ? Environment.ExitCode : 1);
    }

    private void NotificationArea_OpenRequested(object? sender, EventArgs e)
    {
        if (_ownedMainWindow is null)
        {
            return;
        }

        if (!_ownedMainWindow.IsVisible)
        {
            _ownedMainWindow.Show();
        }

        if (_ownedMainWindow.WindowState == WindowState.Minimized)
        {
            _ownedMainWindow.WindowState = WindowState.Normal;
        }

        _ownedMainWindow.Activate();
    }

    private void NotificationArea_ExitRequested(object? sender, EventArgs e)
    {
        if (_ownedMainWindow is null)
        {
            Shutdown();
            return;
        }

        _explicitExitRequested = true;
        _notificationArea?.SetVisible(false);
        _ownedMainWindow.Close();
    }

    private void SyncNotificationAreaVisibility()
    {
        if (_notificationArea is null || _ownedMainWindow is null)
        {
            return;
        }

        bool visible = BackgroundRuntimePolicy.ShouldShowNotificationArea(
            renderSmokeMode: false,
            _ownedMainWindow.KeepsRunningInNotificationArea
                ? CloseBehavior.NotificationArea
                : CloseBehavior.Exit,
            _notificationArea.IsAvailable);
        _notificationArea.SetVisible(visible);
        _ownedMainWindow.SetNotificationAreaAvailability(_notificationArea.IsAvailable);
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
        TaskCompletionSource<bool> shutdownCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.ShutdownCompleted += (_, args) =>
            shutdownCompleted.TrySetResult(args.ResourcesDisposed);
        window.Show();

        DispatcherTimer timer = new(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            int exitCode = 0;
            try
            {
                await window.StartupCompleted.WaitAsync(RuntimeLaunchPolicy.RenderSmokeStartupTimeout);
                await Task.Delay(TimeSpan.FromMilliseconds(250));
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
            catch (Exception exception)
            {
                exitCode = 1;
                try
                {
                    File.WriteAllText(fullOutputPath + ".error.txt", exception.ToString());
                }
                catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
                {
                    // The nonzero process exit still fails the evidence run.
                }
            }
            finally
            {
                Environment.ExitCode = exitCode;
                window.Close();
                bool cleanupSignaled = await OwnedTaskDrain.WaitAsync(
                    TimeSpan.FromSeconds(25),
                    shutdownCompleted.Task);
                bool resourcesDisposed = ShutdownCompletionPolicy.ResourcesWereDisposed(
                    cleanupSignaled,
                    shutdownCompleted.Task);
                if (!resourcesDisposed)
                {
                    exitCode = 1;
                    Environment.ExitCode = exitCode;
                    try
                    {
                        File.WriteAllText(
                            fullOutputPath + ".error.txt",
                            "Soltex render cleanup did not dispose all owned resources within the shutdown bound.");
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The nonzero process exit still fails the evidence run.
                    }
                }

                Shutdown(exitCode);
            }
        };
        timer.Start();
    }

    private async void RunRuntimeProbe(
        MainWindow window,
        string? outputPath,
        string? sourceHeadSha,
        string? testedCommitSha)
    {
        int exitCode = 0;
        string? fullOutputPath = null;
        TaskCompletionSource<bool> shutdownCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.ShutdownCompleted += (_, args) =>
            shutdownCompleted.TrySetResult(args.ResourcesDisposed);
        string? progressPath = null;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceHeadSha);
            ArgumentException.ThrowIfNullOrWhiteSpace(testedCommitSha);
            fullOutputPath = Path.GetFullPath(outputPath);
            progressPath = fullOutputPath + ".progress.log";
            string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(
                    "The runtime-probe output path must include a directory.",
                    nameof(outputPath));
            }

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(
                progressPath,
                $"{DateTimeOffset.UtcNow:O} runtime-probe-start{Environment.NewLine}");
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32_000;
            window.Top = -32_000;
            window.Show();
            RuntimeCostReport report = await RuntimeCostProbe.CaptureAsync(
                window,
                sourceHeadSha,
                testedCommitSha,
                message => File.AppendAllText(
                    progressPath,
                    $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}"));
            File.AppendAllText(
                progressPath,
                $"{DateTimeOffset.UtcNow:O} serialization-start{Environment.NewLine}");
            await using FileStream stream = new(
                fullOutputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            await JsonSerializer.SerializeAsync(
                stream,
                report,
                RuntimeReportJsonOptions);
            await stream.FlushAsync();
            File.AppendAllText(
                progressPath,
                $"{DateTimeOffset.UtcNow:O} serialization-complete{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           InvalidOperationException or
                                           ArgumentException or
                                           TimeoutException or
                                           NotSupportedException)
        {
            exitCode = 1;
            if (!string.IsNullOrWhiteSpace(fullOutputPath))
            {
                try
                {
                    File.WriteAllText(fullOutputPath + ".error.txt", exception.ToString());
                }
                catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
                {
                    // Exit code still reports failure when even the sidecar cannot be written.
                }
            }
        }
        finally
        {
            Environment.ExitCode = exitCode;
            window.Close();
            bool cleanupSignaled = await OwnedTaskDrain.WaitAsync(
                TimeSpan.FromSeconds(25),
                shutdownCompleted.Task);
            bool resourcesDisposed = ShutdownCompletionPolicy.ResourcesWereDisposed(
                cleanupSignaled,
                shutdownCompleted.Task);
            if (!resourcesDisposed)
            {
                exitCode = 1;
                Environment.ExitCode = exitCode;
                if (!string.IsNullOrWhiteSpace(fullOutputPath))
                {
                    try
                    {
                        File.WriteAllText(
                            fullOutputPath + ".error.txt",
                            "Soltex runtime-probe cleanup did not dispose all owned resources within the shutdown bound.");
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The nonzero process exit still fails the evidence run.
                    }
                }
            }

            Shutdown(exitCode);
        }
    }
}
