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
using Microsoft.Win32;
using Soltex.Security;
using Soltex.Whisper;
using Soltex.Whisper.Windows;

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
    private bool _appearanceTrackingStarted;
    private AppearancePreference _appearancePreference = AppearancePreference.System;
    private ResolvedAppearance _resolvedAppearance = ResolvedAppearance.Dark;

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
        if (RuntimeLaunchPolicy.IsWhisperUninstallCleanup(e.Args))
        {
            RunWhisperUninstallCleanup(
                Environment.GetEnvironmentVariable("SOLTEX_WHISPER_UNINSTALL_REPORT_PATH"));
            return;
        }

        if (RuntimeLaunchPolicy.IsWhisperRuntimeProbe(e.Args))
        {
            RunWhisperRuntimeProbe(
                Environment.GetEnvironmentVariable("SOLTEX_WHISPER_RUNTIME_REPORT_PATH"),
                Environment.GetEnvironmentVariable("SOLTEX_SOURCE_HEAD_SHA"),
                Environment.GetEnvironmentVariable("SOLTEX_TESTED_COMMIT_SHA"));
            return;
        }

        if (RuntimeLaunchPolicy.IsWhisperModelProbe(e.Args))
        {
            RunWhisperModelProbe(
                Environment.GetEnvironmentVariable("SOLTEX_WHISPER_MODEL_REPORT_PATH"),
                Environment.GetEnvironmentVariable("SOLTEX_WHISPER_LIVE_AUDIO_PATH"),
                Environment.GetEnvironmentVariable("SOLTEX_WHISPER_LIVE_AUDIO_SHA256"),
                Environment.GetEnvironmentVariable("SOLTEX_SOURCE_HEAD_SHA"),
                Environment.GetEnvironmentVariable("SOLTEX_TESTED_COMMIT_SHA"));
            return;
        }

        bool renderRequestParsed = RuntimeLaunchPolicy.TryParseRenderSmoke(
            e.Args,
            out RenderSmokeRequest? renderRequest);
        bool overlayRequestParsed = RuntimeLaunchPolicy.TryParseWhisperOverlaySmoke(
            e.Args,
            out WhisperOverlaySmokeRequest? overlayRequest);
        if (renderRequestParsed || overlayRequestParsed)
        {
            RenderSmokeAppearance appearance = renderRequestParsed
                ? renderRequest!.Appearance
                : overlayRequest!.Appearance;
            _appearancePreference = RuntimeLaunchPolicy.ResolveRenderPreference(
                appearance);
            _resolvedAppearance = RuntimeLaunchPolicy.ResolveRenderAppearance(
                appearance);
            AppearanceThemeManager.ApplyResolved(Resources, _resolvedAppearance);
        }
        else
        {
            _appearancePreference = PreferencesStore.CreateDefault()
                .Load()
                .Preferences
                .AppearancePreference;
            _resolvedAppearance = AppearanceThemeManager.ApplyPreference(
                Resources,
                _appearancePreference);
        }

        if (overlayRequestParsed)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RenderWhisperOverlaySnapshot(overlayRequest!);
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.UpdateAppearanceResolution(
            _resolvedAppearance,
            SystemParameters.HighContrast);

        if (RuntimeLaunchPolicy.IsRuntimeProbe(e.Args))
        {
            RunRuntimeProbe(
                window,
                Environment.GetEnvironmentVariable("SOLTEX_RUNTIME_REPORT_PATH"),
                Environment.GetEnvironmentVariable("SOLTEX_SOURCE_HEAD_SHA"),
                Environment.GetEnvironmentVariable("SOLTEX_TESTED_COMMIT_SHA"));
            return;
        }

        if (renderRequestParsed)
        {
            if (renderRequest!.Panel is not null &&
                !window.TrySelectRenderSmokePanel(renderRequest.Panel))
            {
                throw new ArgumentException(
                    "The render-smoke panel must be one of: home, monitoring, monitoring-details, monitoring-benchmark, applications, applications-services, settings, devices, " +
                    "security, security-activity, remote, whisper, whisper-personalize, whisper-library, whisper-library-styles, whisper-library-apps, whisper-scratchpad, whisper-history, whisper-privacy, whisper-privacy-warning, activity, update, mixer, mixer-devices, mixer-more, command-palette, clips.");
            }

            RenderSmokeSnapshot(window, renderRequest);
            return;
        }

        if (RuntimeLaunchPolicy.HasControlledRuntimeOption(e.Args))
        {
            throw new ArgumentException(
                "The controlled runtime arguments are incomplete, duplicated, or conflicting.");
        }

        StartNormalRuntime(window);
        StartAppearanceTracking();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopAppearanceTracking();
        _notificationArea?.Dispose();
        _notificationArea = null;
        base.OnExit(e);
    }

    private void RunWhisperUninstallCleanup(string? reportPath)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            ProductDataRootResolution resolution = ProductDataRootResolver.ResolveDefault();
            WhisperUninstallCleanupResult result =
                WindowsWhisperUninstallCleanup.CleanAsync(resolution.ProductRoot)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                WriteWhisperUninstallReport(reportPath, result);
            }

            Environment.ExitCode = 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Environment.ExitCode = 1;
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                WriteWhisperUninstallFailure(reportPath, exception);
            }
        }
        finally
        {
            Shutdown(Environment.ExitCode);
        }
    }

    private static void WriteWhisperUninstallReport(
        string reportPath,
        WhisperUninstallCleanupResult result)
    {
        string fullPath = PrepareFreshReportPath(reportPath);
        var report = new
        {
            schemaVersion = 1,
            outcome = "cleaned",
            result.ModelArtifactRemoved,
            result.InterruptedDownloadsRemoved,
            result.InstallLockRemoved,
            result.SettingsRemoved,
            result.SettingsTemporaryArtifactsRemoved,
            result.EncryptedHistoryRemoved,
            result.ProviderCredentialRemoved,
            contentCaptured = false,
            recordedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(report, RuntimeReportJsonOptions));
    }

    private static void WriteWhisperUninstallFailure(
        string reportPath,
        Exception exception)
    {
        string fullPath = Path.GetFullPath(reportPath) + ".error.txt";
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string category = exception switch
        {
            UnauthorizedAccessException => "access-denied",
            InvalidDataException => "unsafe-state",
            _ => "io-failure"
        };
        File.WriteAllText(
            fullPath,
            "Whisper uninstall cleanup failed safely. category=" + category);
    }

    private static string PrepareFreshReportPath(string reportPath)
    {
        string fullPath = Path.GetFullPath(reportPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("The uninstall report requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(fullPath))
        {
            throw new IOException("The uninstall report already exists.");
        }

        return fullPath;
    }

    internal void SetAppearancePreference(AppearancePreference preference)
    {
        _appearancePreference = Enum.IsDefined(preference)
            ? preference
            : AppearancePreference.System;
        ApplyCurrentAppearance();
    }

    private void StartAppearanceTracking()
    {
        if (_appearanceTrackingStarted)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        _appearanceTrackingStarted = true;
    }

    private void StopAppearanceTracking()
    {
        if (!_appearanceTrackingStarted)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _appearanceTrackingStarted = false;
    }

    private void SystemParameters_StaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(SystemParameters.HighContrast),
                StringComparison.Ordinal))
        {
            QueueAppearanceRefresh();
        }
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or
                          UserPreferenceCategory.General or
                          UserPreferenceCategory.VisualStyle)
        {
            QueueAppearanceRefresh();
        }
    }

    private void QueueAppearanceRefresh()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(ApplyCurrentAppearance));
    }

    private void ApplyCurrentAppearance()
    {
        _resolvedAppearance = AppearanceThemeManager.ApplyPreference(
            Resources,
            _appearancePreference);
        _ownedMainWindow?.UpdateAppearanceResolution(
            _resolvedAppearance,
            SystemParameters.HighContrast);
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

    private void RenderSmokeSnapshot(MainWindow window, RenderSmokeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string fullOutputPath = Path.GetFullPath(request.OutputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException(
                "The render-smoke output path must include a directory.",
                nameof(request));
        }

        Directory.CreateDirectory(outputDirectory);
        RenderSmokeViewport viewport = RuntimeLaunchPolicy.ResolveRenderViewport(
            request.Profile);
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        RenderSmokeCapture.ConfigureWindow(window, viewport);
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
                RenderTargetBitmap bitmap = RenderSmokeCapture.Capture(window, viewport);
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

    private void RenderWhisperOverlaySnapshot(WhisperOverlaySmokeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!WhisperOverlayEvidenceCatalog.TryProject(
                request.State,
                out WhisperOverlayView? frame) ||
            frame is null)
        {
            throw new ArgumentException(
                "The Whisper overlay state is not part of the bounded evidence catalog.",
                nameof(request));
        }

        string fullOutputPath = Path.GetFullPath(request.OutputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException(
                "The Whisper overlay output path must include a directory.",
                nameof(request));
        }

        Directory.CreateDirectory(outputDirectory);
        WhisperOverlayWindow window = new()
        {
            Left = -32_000,
            Top = -32_000,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        window.Render(frame);
        if (frame.ShowLevelMeter)
        {
            window.SetInputLevel(0.62d);
        }

        DispatcherTimer timer = new(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            int exitCode = 0;
            try
            {
                RenderSmokeViewport viewport = RenderSmokeCapture.CreateOverlayViewport(
                    window,
                    request.ScalePercent);
                RenderTargetBitmap bitmap = RenderSmokeCapture.Capture(window, viewport);
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = new(
                    fullOutputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
            }
            catch (Exception exception)
            {
                exitCode = 1;
                try
                {
                    File.WriteAllText(fullOutputPath + ".error.txt", exception.ToString());
                }
                catch (Exception writeException) when (writeException is IOException or
                                                       UnauthorizedAccessException)
                {
                    // The nonzero process exit still fails the evidence run.
                }
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

    private void RunWhisperRuntimeProbe(
        string? outputPath,
        string? sourceHeadSha,
        string? testedCommitSha)
    {
        int exitCode = 0;
        string? fullOutputPath = null;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceHeadSha);
            ArgumentException.ThrowIfNullOrWhiteSpace(testedCommitSha);
            string source = RuntimeCostProbe.ValidateCommit(sourceHeadSha);
            string tested = RuntimeCostProbe.ValidateCommit(testedCommitSha);
            fullOutputPath = Path.GetFullPath(outputPath);
            string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(
                    "The Whisper runtime-probe output path must include a directory.",
                    nameof(outputPath));
            }

            Directory.CreateDirectory(outputDirectory);
            WhisperLocalRuntimeProbeResult result = WhisperLocalRuntimeProbe.Run();
            using FileStream stream = new(
                fullOutputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            JsonSerializer.Serialize(
                stream,
                new
                {
                    schemaVersion = 1,
                    sourceHeadSha = source,
                    testedCommitSha = tested,
                    providerId = result.ProviderId,
                    runtimeId = result.RuntimeId,
                    available = result.Available,
                    modelOpened = false,
                    microphoneOpened = false
                },
                RuntimeReportJsonOptions);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           InvalidOperationException or
                                           ArgumentException or
                                           NotSupportedException)
        {
            exitCode = 1;
            if (!string.IsNullOrWhiteSpace(fullOutputPath))
            {
                try
                {
                    File.WriteAllText(
                        fullOutputPath + ".error.txt",
                        "The packaged local CPU transcription runtime probe failed.");
                }
                catch (Exception writeException) when (writeException is IOException or
                                                       UnauthorizedAccessException)
                {
                    // The nonzero process exit remains the authoritative failure signal.
                }
            }
        }
        finally
        {
            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
        }
    }

    private void RunWhisperModelProbe(
        string? outputPath,
        string? audioFixturePath,
        string? expectedAudioSha256,
        string? sourceHeadSha,
        string? testedCommitSha)
    {
        int exitCode = 0;
        string? fullOutputPath = null;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(audioFixturePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudioSha256);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceHeadSha);
            ArgumentException.ThrowIfNullOrWhiteSpace(testedCommitSha);
            string source = RuntimeCostProbe.ValidateCommit(sourceHeadSha);
            string tested = RuntimeCostProbe.ValidateCommit(testedCommitSha);
            fullOutputPath = Path.GetFullPath(outputPath);
            string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(
                    "The Whisper model-probe output path must include a directory.",
                    nameof(outputPath));
            }

            Directory.CreateDirectory(outputDirectory);
            ProductDataRootResolution resolution = ProductDataRootResolver.ResolveDefault();
            using CancellationTokenSource timeout = new(
                WindowsWhisperLocalModelProbe.DefaultTimeout);
            WhisperLocalModelProbeResult result = WindowsWhisperLocalModelProbe
                .RunAsync(
                    resolution.ProductRoot,
                    audioFixturePath,
                    expectedAudioSha256,
                    timeout.Token)
                .GetAwaiter()
                .GetResult();
            using FileStream stream = new(
                fullOutputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            JsonSerializer.Serialize(
                stream,
                new
                {
                    schemaVersion = 1,
                    sourceHeadSha = source,
                    testedCommitSha = tested,
                    providerId = WhisperLocalModelDefaults.ProviderId,
                    modelId = WhisperLocalModelDefaults.ModelId,
                    runtimeId = WhisperLocalModelDefaults.RuntimeId,
                    modelBytes = result.ModelBytes,
                    fixtureSha256 = expectedAudioSha256.ToLowerInvariant(),
                    audioDurationMilliseconds = result.AudioDurationMilliseconds,
                    initialTranscriptionMilliseconds = result.InitialTranscriptionMilliseconds,
                    initialUnloadMilliseconds = result.InitialUnloadMilliseconds,
                    restartTranscriptionMilliseconds = result.RestartTranscriptionMilliseconds,
                    cancellationMilliseconds = result.CancellationMilliseconds,
                    cancellationUnloadMilliseconds = result.CancellationUnloadMilliseconds,
                    workingSetBeforeBytes = result.WorkingSetBeforeBytes,
                    peakWorkingSetBytes = result.PeakWorkingSetBytes,
                    workingSetAfterBytes = result.WorkingSetAfterBytes,
                    restartSucceeded = result.RestartSucceeded,
                    cancellationObserved = result.CancellationObserved,
                    modelOpened = true,
                    microphoneOpened = false,
                    audioLogged = false,
                    transcriptLogged = false
                },
                RuntimeReportJsonOptions);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           InvalidOperationException or
                                           ArgumentException or
                                           NotSupportedException or
                                           OperationCanceledException)
        {
            exitCode = 1;
            if (!string.IsNullOrWhiteSpace(fullOutputPath))
            {
                try
                {
                    File.WriteAllText(
                        fullOutputPath + ".error.txt",
                        "The packaged local-model transcription probe failed.");
                }
                catch (Exception writeException) when (writeException is IOException or
                                                       UnauthorizedAccessException)
                {
                    // The nonzero process exit remains the authoritative failure signal.
                }
            }
        }
        finally
        {
            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
        }
    }
}
