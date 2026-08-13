using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Soltex.App;
using Soltex.App.Controls;
using Soltex.App.Views;
using Soltex.Audio;
using Soltex.Benchmarks;
using Soltex.DeviceFabric;
using Soltex.Monitoring;

namespace Soltex.App.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        global::Soltex.App.App application = new();
        application.InitializeComponent();
        LocalDeviceObservation device = LocalDeviceObservationProvider.Capture();
        SystemTelemetrySnapshot snapshot = SystemTelemetryProvider.CaptureAsync(
            TimeSpan.FromMilliseconds(150),
            CancellationToken.None).GetAwaiter().GetResult();
        AudioEndpointSnapshot audioSnapshot = AudioEndpointProvider.CaptureAsync().GetAwaiter().GetResult();
        AudioSessionSnapshot audioSessionSnapshot = CreateAudioSessionSnapshot();
        ApplicationInventorySnapshot applicationSnapshot =
            ApplicationInventoryProvider.CaptureAsync().GetAwaiter().GetResult();
        WindowsServiceInventorySnapshot serviceSnapshot =
            WindowsServiceInventoryProvider.CaptureAsync().GetAwaiter().GetResult();

        List<(string Name, Action Test)> tests =
        [
            ("Shared theme exposes required control resources", ThemeResourcesAreAvailable),
            ("Workspace command catalog is bounded and searchable", WorkspaceCommandsAreBounded),
            ("Telemetry runs only in visible live workspaces", TelemetryRunsOnlyInLiveWorkspaces),
            ("Telemetry loop ownership serializes duplicate stop and queued restart", TelemetryLoopOwnershipIsSerialized),
            ("Background runtime remains explicit and fail-closed", BackgroundRuntimeIsExplicit),
            ("Notification-area resource has a bounded show-hide-dispose lifecycle", NotificationAreaLifecycleIsBounded),
            ("Owned startup work drains before resource disposal", OwnedStartupWorkDrains),
            ("Shutdown evidence requires confirmed resource disposal", ShutdownEvidenceRequiresDisposal),
            ("Runtime CPU cost math is processor-normalized and bounded", RuntimeCostMathIsBounded),
            ("Runtime probe launch parsing is bounded and conflict-aware", RuntimeLaunchParsingIsBounded),
            ("Process actions reject Windows and Soltex targets", ProcessActionsRejectProtectedTargets),
            ("Process actions reject identity drift", ProcessActionsRejectIdentityDrift),
            ("Process actions admit only the selected user-session process", ProcessActionsAdmitBoundedTarget),
            ("Sparkline renders bounded percentage values", SparklineRenders),
            ("Sparkline auto-scales unbounded throughput values", SparklineAutoScales),
            ("Home view renders a live snapshot", () => HomeViewRenders(snapshot, device)),
            ("Monitoring view renders provenance and bounded rows", () => MonitoringViewRenders(snapshot)),
            ("Monitoring details are disclosed only on request", MonitoringDetailsAreProgressive),
            ("Benchmark lab is explicit, bounded, and progressively disclosed", BenchmarkLabIsProgressive),
            ("Benchmark execution is limited to the visible lab", BenchmarkExecutionRequiresVisibleLab),
            ("Benchmark result store is bounded, atomic, and recoverable", BenchmarkResultStoreIsRecoverable),
            ("Application inventory is bounded and path-free", () => ApplicationInventoryIsBounded(applicationSnapshot)),
            ("Windows service inventory is bounded and read-only", () => ServiceInventoryIsBounded(serviceSnapshot)),
            ("Applications view progressively discloses startup and services", () =>
                ApplicationsViewRenders(applicationSnapshot, serviceSnapshot)),
            ("Preference store recovers and round-trips bounded local state", PreferencesRoundTripAndRecovery),
            ("Activity store bounds, sanitizes, persists, and recovers", ActivityStoreBoundsAndRecovers),
            ("Activity view renders and filters meaningful events", ActivityViewRenders),
            ("Settings view renders working local preferences", SettingsViewRenders),
            ("Mixer prioritizes active endpoints and bounded app controls", () =>
                MixerPrioritizesActiveEndpoints(audioSnapshot, audioSessionSnapshot)),
            ("Audio mix snapshot is bounded, exact-match, and recoverable", AudioMixSnapshotIsBoundedAndRecoverable),
            ("Mixer keeps fallback reminders local and user mediated", MixerFallbackRemindersAreBounded),
            ("Windows Sound handoff uses one fixed supported URI", WindowsSoundHandoffIsFixed),
            ("Devices view renders an explicit unenrolled profile", () => DevicesViewRenders(device)),
            ("Render-smoke uses an unconstrained popup viewport", RenderSmokeUsesCanonicalViewport)
        ];

        int failed = 0;
        Stopwatch suite = Stopwatch.StartNew();
        foreach ((string name, Action test) in tests)
        {
            Stopwatch testTimer = Stopwatch.StartNew();
            try
            {
                test();
                testTimer.Stop();
                Console.WriteLine($"PASS  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
            }
            catch (Exception exception)
            {
                testTimer.Stop();
                failed++;
                Console.WriteLine($"FAIL  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
                Console.WriteLine("      " + exception.Message);
            }
        }

        suite.Stop();
        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
        Console.WriteLine($"MEASURE app_control_suite tests={tests.Count} failed={failed} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
        return failed == 0 ? 0 : 1;
    }

    private static void ThemeResourcesAreAvailable()
    {
        ResourceDictionary resources = Application.Current.Resources;
        True(resources.Contains("AccentBrush"), "The shared accent brush is missing.");
        True(resources.Contains("HeroCardStyle"), "The shared hero-card style is missing.");
        True(resources.Contains("SoltexSliderStyle"), "The shared slider style is missing.");
    }

    private static void TelemetryRunsOnlyInLiveWorkspaces()
    {
        True(TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: false,
            WindowState.Normal,
            homeVisible: true,
            monitoringVisible: false),
            "Home should keep telemetry active.");
        True(TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: false,
            WindowState.Maximized,
            homeVisible: false,
            monitoringVisible: true),
            "Monitoring should keep telemetry active.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: false,
            WindowState.Normal,
            homeVisible: false,
            monitoringVisible: false),
            "Hidden live workspaces must suspend telemetry.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: false,
            WindowState.Minimized,
            homeVisible: true,
            monitoringVisible: false),
            "A minimized window must suspend telemetry.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: true,
            WindowState.Normal,
            homeVisible: true,
            monitoringVisible: false),
            "Closing must prevent telemetry restart.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: false,
            isClosing: false,
            WindowState.Normal,
            homeVisible: true,
            monitoringVisible: false),
            "A hidden notification-area window must suspend performance telemetry.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isVisible: true,
            isClosing: false,
            WindowState.Normal,
            homeVisible: false,
            monitoringVisible: true,
            benchmarkVisible: true),
            "The benchmark lab must suspend competing live telemetry sampling.");
    }

    private static void WorkspaceCommandsAreBounded()
    {
        IReadOnlyList<WorkspaceCommand> all = WorkspaceCommandCatalog.Query(null);
        True(all.Count == 9, "The command surface must expose the nine supported workspaces exactly once.");
        True(all.Select(command => command.Workspace).Distinct(StringComparer.Ordinal).Count() == all.Count,
            "Workspace commands contained duplicate routes.");
        True(all.Select(command => command.Shortcut).Distinct(StringComparer.Ordinal).Count() == all.Count,
            "Workspace commands contained duplicate shortcuts.");

        IReadOnlyList<WorkspaceCommand> audio = WorkspaceCommandCatalog.Query("volume sessions");
        True(audio.Count == 1 && audio[0].Workspace == "mixer",
            "The command query did not route Audio synonyms to the mixer workspace.");
        IReadOnlyList<WorkspaceCommand> remote = WorkspaceCommandCatalog.Query("screen peer");
        True(remote.Count == 1 && remote[0].Workspace == "remote",
            "The command query did not route Remote Assist synonyms.");
        True(WorkspaceCommandCatalog.Query("not-a-soltex-tool").Count == 0,
            "An unknown command query produced a fabricated result.");
    }

    private static void TelemetryLoopOwnershipIsSerialized()
    {
        TelemetryLoopOwner owner = new();
        TaskCompletionSource<bool> firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> firstCancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseFirstLoop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        owner.StartAsync(async cancellationToken =>
        {
            firstStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                firstCancellationObserved.TrySetResult(true);
                await releaseFirstLoop.Task;
                throw;
            }
        }).GetAwaiter().GetResult();
        firstStarted.Task.GetAwaiter().GetResult();
        True(owner.IsActive, "Telemetry ownership did not report its first loop as active.");

        Task firstStop = owner.StopAsync();
        firstCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Task duplicateStop = owner.StopAsync();
        True(!duplicateStop.IsCompleted,
            "A duplicate telemetry stop bypassed the in-flight owner's serialized cleanup.");
        releaseFirstLoop.TrySetResult(true);
        Task.WhenAll(firstStop, duplicateStop).GetAwaiter().GetResult();
        True(!owner.IsActive, "Concurrent telemetry stop left a disposed source published as active.");

        TaskCompletionSource<bool> secondStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondCancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseSecondLoop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        owner.StartAsync(async cancellationToken =>
        {
            secondStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                secondCancellationObserved.TrySetResult(true);
                await releaseSecondLoop.Task;
                throw;
            }
        }).GetAwaiter().GetResult();
        secondStarted.Task.GetAwaiter().GetResult();

        Task secondStop = owner.StopAsync();
        secondCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        TaskCompletionSource<bool> restarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task queuedRestart = owner.StartAsync(async cancellationToken =>
        {
            restarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        True(!queuedRestart.IsCompleted && !restarted.Task.IsCompleted,
            "A telemetry restart bypassed cleanup of the previous owned loop.");
        releaseSecondLoop.TrySetResult(true);
        Task.WhenAll(secondStop, queuedRestart).GetAwaiter().GetResult();
        restarted.Task.GetAwaiter().GetResult();
        True(owner.IsActive, "Telemetry ownership could not restart after a completed stop.");
        owner.StopAsync().GetAwaiter().GetResult();
        True(!owner.IsActive, "Telemetry ownership did not release its restarted loop.");
    }

    private static void BackgroundRuntimeIsExplicit()
    {
        True(!BackgroundRuntimePolicy.ShouldHideOnClose(
            renderSmokeMode: false,
            explicitExitRequested: false,
            CloseBehavior.Exit,
            notificationAreaAvailable: true),
            "The default Exit policy unexpectedly hid the window.");
        True(BackgroundRuntimePolicy.ShouldHideOnClose(
            renderSmokeMode: false,
            explicitExitRequested: false,
            CloseBehavior.NotificationArea,
            notificationAreaAvailable: true),
            "Explicit notification-area mode did not retain the process.");
        True(!BackgroundRuntimePolicy.ShouldHideOnClose(
            renderSmokeMode: false,
            explicitExitRequested: true,
            CloseBehavior.NotificationArea,
            notificationAreaAvailable: true),
            "Explicit Exit was ignored by notification-area mode.");
        True(!BackgroundRuntimePolicy.ShouldHideOnClose(
            renderSmokeMode: false,
            explicitExitRequested: false,
            CloseBehavior.NotificationArea,
            notificationAreaAvailable: false),
            "An unavailable notification area did not fail closed to Exit.");
        True(!BackgroundRuntimePolicy.ShouldShowNotificationArea(
            renderSmokeMode: true,
            CloseBehavior.NotificationArea,
            notificationAreaAvailable: true),
            "Render smoke attempted to create a notification-area surface.");
    }

    private static void NotificationAreaLifecycleIsBounded()
    {
        HwndSourceParameters parameters = new("Soltex notification-area lifecycle test")
        {
            Width = 1,
            Height = 1,
            WindowStyle = unchecked((int)0x80000000)
        };
        using HwndSource source = new(parameters);
        using NotificationAreaController controller = new(source.Handle);
        True(controller.IsAvailable,
            "The Windows notification-area resource was unavailable after construction.");
        controller.SetVisible(visible: true);
        True(controller.IsVisible,
            "The notification-area resource did not expose its visible state.");
        controller.SetVisible(visible: false);
        True(!controller.IsVisible,
            "The notification-area resource did not hide synchronously.");
    }

    private static void RuntimeCostMathIsBounded()
    {
        Near(
            12.5,
            RuntimeCostMath.NormalizeCpuPercent(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                processorCount: 4),
            0.001);
        Near(
            0,
            RuntimeCostMath.NormalizeCpuPercent(
                TimeSpan.FromSeconds(-1),
                TimeSpan.FromSeconds(2),
                processorCount: 4),
            0.001);
        Near(
            100,
            RuntimeCostMath.NormalizeCpuPercent(
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(1),
                processorCount: 1),
            0.001);
        True(RuntimeCostProbe.ValidateCommit(new string('A', 40)) == new string('a', 40),
            "Runtime evidence did not normalize a valid commit identity.");

        IReadOnlyDictionary<int, TimeSpan> threadCpu = RuntimeCostProbe.CalculateThreadCpu(
            new Dictionary<int, TimeSpan>
            {
                [11] = TimeSpan.FromMilliseconds(100),
                [12] = TimeSpan.FromMilliseconds(20)
            },
            new Dictionary<int, TimeSpan>
            {
                [11] = TimeSpan.FromMilliseconds(130),
                [12] = TimeSpan.FromMilliseconds(10),
                [13] = TimeSpan.FromMilliseconds(5)
            });
        Near(30, threadCpu[11].TotalMilliseconds, 0.001);
        Near(0, threadCpu[12].TotalMilliseconds, 0.001);
        Near(5, threadCpu[13].TotalMilliseconds, 0.001);
    }

    private static void OwnedStartupWorkDrains()
    {
        True(OwnedTaskDrain.WaitAsync(
                TimeSpan.FromSeconds(1),
                Task.CompletedTask).GetAwaiter().GetResult(),
            "Completed startup work was not recognized as drained.");

        TaskCompletionSource cancelled = new();
        cancelled.SetCanceled();
        True(OwnedTaskDrain.WaitAsync(
                TimeSpan.FromSeconds(1),
                cancelled.Task).GetAwaiter().GetResult(),
            "Cancelled startup work was not recognized as drained.");

        True(OwnedTaskDrain.WaitAsync(
                TimeSpan.FromSeconds(1),
                Task.FromException(new InvalidOperationException("fixture")))
            .GetAwaiter().GetResult(),
            "Faulted startup work was not recognized as drained.");

        TaskCompletionSource pending = new();
        True(!OwnedTaskDrain.WaitAsync(
                TimeSpan.FromMilliseconds(20),
                pending.Task).GetAwaiter().GetResult(),
            "Pending startup work ignored the shutdown-drain bound.");
        pending.TrySetResult();
    }

    private static void ShutdownEvidenceRequiresDisposal()
    {
        True(ShutdownCompletionPolicy.ResourcesWereDisposed(
                completionSignaled: true,
                Task.FromResult(true)),
            "A confirmed resource-disposal signal was rejected.");
        True(!ShutdownCompletionPolicy.ResourcesWereDisposed(
                completionSignaled: true,
                Task.FromResult(false)),
            "An abandoned cleanup was accepted as successful shutdown.");
        True(!ShutdownCompletionPolicy.ResourcesWereDisposed(
                completionSignaled: false,
                Task.FromResult(true)),
            "An unobserved completion was accepted as successful shutdown.");
        TaskCompletionSource<bool> pending = new();
        True(!ShutdownCompletionPolicy.ResourcesWereDisposed(
                completionSignaled: true,
                pending.Task),
            "Pending cleanup was accepted as successful shutdown.");
        pending.TrySetResult(true);
    }

    private static void RuntimeLaunchParsingIsBounded()
    {
        True(RuntimeLaunchPolicy.IsRuntimeProbe(["--runtime-probe"]),
            "The standalone runtime-probe option was not recognized.");
        True(RuntimeLaunchPolicy.IsRuntimeProbe(["--future-option", "--runtime-probe"]),
            "A harmless unrelated option prevented runtime-probe dispatch.");
        True(!RuntimeLaunchPolicy.IsRuntimeProbe(["--runtime-probe", "--runtime-probe"]),
            "Duplicate runtime-probe options were accepted.");
        True(!RuntimeLaunchPolicy.IsRuntimeProbe(["--runtime-probe", "--render-smoke"]),
            "Conflicting deterministic runtime modes were accepted.");
        True(RuntimeLaunchPolicy.HasControlledRuntimeOption(
                ["--runtime-probe", "--render-smoke"]),
            "A conflicting controlled-runtime request could fall through to normal startup.");
        True(!RuntimeLaunchPolicy.HasControlledRuntimeOption(["--ordinary-option"]),
            "An ordinary option was mistaken for a controlled runtime request.");
        True(!RuntimeLaunchPolicy.UsesSoftwareRendering(["--runtime-probe"]),
            "The runtime-cost probe incorrectly forced software rendering.");
        True(RuntimeLaunchPolicy.UsesSoftwareRendering(["--render-smoke", "image.png"]),
            "Native render smoke did not retain deterministic software rendering.");
        True(RuntimeLaunchPolicy.RenderSmokeStartupTimeout == TimeSpan.FromSeconds(40),
            "Render-smoke startup no longer covers the composed bounded protection query while remaining finite.");
    }

    private static void ProcessActionsRejectProtectedTargets()
    {
        ProcessActionPolicyDecision system = ProcessActionPolicy.EvaluateTarget(
            new ProcessActionRequest(4, "System"), 7000, 2, 0, "System");
        True(!system.Allowed, "PID 4 must always be rejected.");

        ProcessActionPolicyDecision defender = ProcessActionPolicy.EvaluateTarget(
            new ProcessActionRequest(640, "MsMpEng"), 7000, 2, 2, "MsMpEng");
        True(!defender.Allowed, "The Defender engine must be rejected.");

        ProcessActionPolicyDecision ownProcess = ProcessActionPolicy.EvaluateTarget(
            new ProcessActionRequest(7000, "Soltex"), 7000, 2, 2, "Soltex");
        True(!ownProcess.Allowed, "Soltex must not end itself.");
    }

    private static void ProcessActionsRejectIdentityDrift()
    {
        ProcessActionPolicyDecision renamed = ProcessActionPolicy.EvaluateTarget(
            new ProcessActionRequest(9000, "notepad"), 7000, 2, 2, "calculator");
        True(!renamed.Allowed, "A changed process name must be rejected.");

        ProcessActionTicket ticket = new(9000, "notepad", 2, 12345);
        ProcessActionPolicyDecision recycled = ProcessActionPolicy.RevalidateTicket(
            ticket, 7000, 2, 2, "notepad", 67890);
        True(!recycled.Allowed, "A recycled PID with a different start time must be rejected.");
    }

    private static void ProcessActionsAdmitBoundedTarget()
    {
        ProcessActionPolicyDecision allowed = ProcessActionPolicy.EvaluateTarget(
            new ProcessActionRequest(9000, "notepad"), 7000, 2, 2, "notepad");
        True(allowed.Allowed, "A matching noncritical process in the current user session should be eligible.");

        ProcessActionTicket ticket = new(9000, "notepad", 2, 12345);
        ProcessActionPolicyDecision revalidated = ProcessActionPolicy.RevalidateTicket(
            ticket, 7000, 2, 2, "notepad", 12345);
        True(revalidated.Allowed, "The exact confirmed process instance should revalidate.");
    }

    private static void SparklineRenders()
    {
        Sparkline sparkline = new()
        {
            Values = [0, 18, 43, 37, 82, 64, 100],
            Stroke = Brushes.Coral,
            Fill = new SolidColorBrush(Color.FromArgb(40, 247, 111, 83)),
            StrokeThickness = 2
        };
        byte[] pixels = Render(sparkline, 320, 90);
        True(CountVisiblePixels(pixels) > 50, "The sparkline render did not produce visible pixels.");
    }

    private static void SparklineAutoScales()
    {
        Sparkline sparkline = new()
        {
            Values = [0, 4_096, 2_048, 16_384, 8_192],
            AutoScale = true,
            Stroke = Brushes.LightGreen,
            Fill = new SolidColorBrush(Color.FromArgb(32, 126, 208, 167)),
            StrokeThickness = 2
        };
        byte[] pixels = Render(sparkline, 320, 90);
        True(CountVisiblePixels(pixels) > 50, "The auto-scaled sparkline did not produce visible pixels.");
    }

    private static void HomeViewRenders(SystemTelemetrySnapshot snapshot, LocalDeviceObservation device)
    {
        HomeView view = new();
        view.UpdateSnapshot(snapshot, device);
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000, "The Home view render was unexpectedly empty.");
        True(view.HomeStateText.Text.Length > 0, "Home did not expose a telemetry state.");
        True(view.MachineNameText.Text == device.DisplayName, "Home did not render the observed local device.");
        True(view.NetworkStatusText.Text is "LIVE" or "UNAVAILABLE", "Home did not expose the network observation state.");
        True(view.HomeHeroCard.ActualHeight <= 266, "The Home hero exceeded its bounded viewport height.");
        True(view.MachineProfileCard.ActualWidth >= 220, "The Home machine profile collapsed below its usable width.");
    }

    private static void MonitoringViewRenders(SystemTelemetrySnapshot snapshot)
    {
        MonitoringView view = new();
        view.UpdateSnapshot(snapshot);
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000, "The Monitoring view render was unexpectedly empty.");
        True(view.ProcessGrid.Items.Count <= SystemTelemetryProvider.MaximumProcessCount, "Monitoring exceeded the process-row bound.");
        True(view.MonitoringProvenanceText.Text.Contains("GetSystemTimes", StringComparison.Ordinal), "Monitoring omitted provider provenance.");
        True(view.MonitoringProvenanceText.Text.Contains("GPU", StringComparison.Ordinal), "Monitoring omitted the GPU limitation.");
        True(view.NetworkCoverageText.Text is "SAMPLED" or "UNAVAILABLE", "Monitoring did not expose the network provider state.");
        True(!view.EndTaskButton.IsEnabled, "End task must remain disabled until a process is selected.");
        True(view.ProcessActionPanel.Visibility == Visibility.Collapsed, "Process action feedback must be quiet by default.");
    }

    private static void MonitoringDetailsAreProgressive()
    {
        MonitoringView view = new();
        True(view.MonitoringOverviewPanel.Visibility == Visibility.Visible,
            "Monitoring overview must be visible on first view.");
        True(view.MonitoringDetailsPanel.Visibility == Visibility.Collapsed,
            "Monitoring detail must be collapsed on first view.");
        view.MonitoringDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.MonitoringOverviewPanel.Visibility == Visibility.Collapsed,
            "Monitoring overview remained visible behind system detail.");
        True(view.MonitoringDetailsPanel.Visibility == Visibility.Visible,
            "Monitoring detail did not open from its explicit disclosure control.");
        view.MonitoringDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.MonitoringOverviewPanel.Visibility == Visibility.Visible,
            "Monitoring overview did not return after closing system detail.");
        True(view.MonitoringDetailsPanel.Visibility == Visibility.Collapsed,
            "Monitoring detail did not close from its disclosure control.");
    }

    private static void ApplicationInventoryIsBounded(ApplicationInventorySnapshot snapshot)
    {
        True(snapshot.CaptureDuration < TimeSpan.FromSeconds(5),
            "Application inventory exceeded five seconds.");
        True(snapshot.Installed.Count <= ApplicationInventoryProvider.MaximumInstalledApplicationCount,
            "Installed application inventory exceeded its bound.");
        True(snapshot.Startup.Count <= ApplicationInventoryProvider.MaximumStartupApplicationCount,
            "Startup inventory exceeded its bound.");
        foreach (InstalledApplicationObservation application in snapshot.Installed)
        {
            True(application.Name.Length is > 0 and <= ApplicationInventoryProvider.MaximumLabelLength,
                "Installed application name is outside bounds.");
            True(!application.Name.Any(char.IsControl),
                "Installed application name contains control characters.");
        }
        foreach (StartupApplicationObservation startup in snapshot.Startup)
        {
            True(startup.Name.Length is > 0 and <= ApplicationInventoryProvider.MaximumLabelLength,
                "Startup entry name is outside bounds.");
            True(!startup.Name.Any(char.IsControl),
                "Startup entry name contains control characters.");
        }
        True(
            ApplicationInventoryProvider.SanitizeLabel("  App\r\nName  ") == "AppName",
            "Application labels were not sanitized deterministically.");
    }

    private static void ServiceInventoryIsBounded(WindowsServiceInventorySnapshot snapshot)
    {
        Console.WriteLine(
            $"      state={snapshot.State}; exposed={snapshot.Services.Count}; observed={snapshot.ObservedServiceCount}; " +
            $"inaccessible={snapshot.InaccessibleConfigurationCount}; omitted={snapshot.OmittedServiceCount}; " +
            $"capture_ms={snapshot.CaptureDuration.TotalMilliseconds:F1}");
        True(snapshot.CaptureDuration < TimeSpan.FromSeconds(5),
            "Windows service inventory exceeded five seconds.");
        True(snapshot.Services.Count <= WindowsServiceInventoryProvider.MaximumServiceCount,
            "Windows service inventory exceeded its public bound.");
        True(snapshot.ObservedServiceCount <= WindowsServiceInventoryProvider.MaximumObservedServiceCount,
            "Windows service inventory exceeded its observation bound.");
        True(snapshot.Provenance.Contains("Service Control Manager", StringComparison.Ordinal),
            "Windows service provenance was omitted.");
        foreach (WindowsServiceObservation service in snapshot.Services)
        {
            True(service.Name.Length is > 0 and <= WindowsServiceInventoryProvider.MaximumNameLength,
                "A service name is outside bounds.");
            True(service.DisplayName.Length is > 0 and <= WindowsServiceInventoryProvider.MaximumDisplayNameLength,
                "A service display name is outside bounds.");
            True(!service.Name.Any(char.IsControl) && !service.DisplayName.Any(char.IsControl),
                "A service label contains control characters.");
            True(!service.DisplayName.Contains(":\\", StringComparison.Ordinal),
                "A service display name exposed a drive-qualified path.");
        }

        True(
            WindowsServiceInventoryProvider.SanitizeLabel(
                @"C:\private\service.exe",
                WindowsServiceInventoryProvider.MaximumDisplayNameLength,
                "Fallback") == "Fallback",
            "Service labels did not reject a drive-qualified path.");
        True(WindowsServiceInventoryProvider.ClassifySignal(1, "Automatic", 1067, 0) == "Review",
            "An automatic stopped service with an unusual exit error was not distinguished.");
        True(WindowsServiceInventoryProvider.ClassifySignal(1, "Manual", 1067, 0).Length == 0,
            "A manual stopped service was overstated as unhealthy.");
        True(WindowsServiceInventoryProvider.ClassifySignal(1, "Automatic", 1077, 0).Length == 0,
            "A service not started since boot was overstated as unhealthy.");
    }

    private static void BenchmarkLabIsProgressive()
    {
        MonitoringView view = new();
        True(view.BenchmarkPanel.Visibility == Visibility.Collapsed &&
             !view.IsBenchmarkVisible,
            "Benchmark lab must be quiet on first view.");
        int modeChanges = 0;
        int runRequests = 0;
        int cancelRequests = 0;
        int clearRequests = 0;
        view.BenchmarkModeChanged += (_, _) => modeChanges++;
        view.BenchmarkRunRequested += (_, _) => runRequests++;
        view.BenchmarkCancelRequested += (_, _) => cancelRequests++;
        view.BenchmarkClearRequested += (_, _) => clearRequests++;
        view.BenchmarkModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.BenchmarkPanel.Visibility == Visibility.Visible &&
             view.MonitoringOverviewPanel.Visibility == Visibility.Collapsed &&
             view.IsBenchmarkVisible &&
             modeChanges == 1,
            "Benchmark lab did not open through its local mode control.");
        view.RunBenchmarkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(runRequests == 1, "Benchmark run did not require the explicit run control.");
        view.ShowBenchmarkRunning("Measuring bounded workloads.");
        True(!view.RunBenchmarkButton.IsEnabled && view.CancelBenchmarkButton.IsEnabled,
            "Running benchmark did not expose a cancel-only action state.");
        view.CancelBenchmarkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(cancelRequests == 1, "Benchmark cancellation did not use its explicit control.");

        view.UpdateBenchmarkResult(CreateBenchmarkResult(), "Saved local result.");
        True(view.BenchmarkStateText.Text == "MEASURED" &&
             view.BenchmarkCpuValueText.Text.Contains("1234", StringComparison.Ordinal) &&
             view.BenchmarkStorageValueText.Text.Contains("400 / 900", StringComparison.Ordinal),
            "Benchmark result did not render named measured values.");
        view.ClearBenchmarkResultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(clearRequests == 1, "Saved benchmark result did not require its explicit clear control.");
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The Benchmark lab render was unexpectedly empty.");
        view.BenchmarkModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.MonitoringOverviewPanel.Visibility == Visibility.Visible &&
             view.BenchmarkPanel.Visibility == Visibility.Collapsed &&
             modeChanges == 2,
            "Benchmark lab did not return to the live Performance view.");
    }

    private static void BenchmarkResultStoreIsRecoverable()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "soltex-benchmark-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "benchmark-result.json");
            BenchmarkResultStore store = new(path);
            BenchmarkResult expected = CreateBenchmarkResult();
            store.Save(expected);
            BenchmarkResultLoad loaded = store.Load();
            True(!loaded.RecoveredFromInvalid && loaded.Result is not null,
                "A valid benchmark result did not round-trip.");
            True(loaded.Result!.Cpu.Value == expected.Cpu.Value &&
                 loaded.Result.StorageRead.Value == expected.StorageRead.Value,
                "Saved benchmark metrics changed during round-trip.");

            File.WriteAllText(path, "{\"schemaVersion\":1");
            BenchmarkResultLoad truncated = store.Load();
            True(truncated.RecoveredFromInvalid && truncated.Result is null,
                "Truncated benchmark state did not fail closed.");
            store.Save(expected);
            string changedJson = File.ReadAllText(path).Replace(
                "\"profileVersion\": 1",
                "\"profileVersion\": 99",
                StringComparison.Ordinal);
            File.WriteAllText(path, changedJson);
            BenchmarkResultLoad changed = store.Load();
            True(changed.RecoveredFromInvalid && changed.Result is null,
                "Changed benchmark state did not fail closed.");
            File.WriteAllText(path, new string('x', BenchmarkResultStore.MaximumDocumentBytes + 1));
            BenchmarkResultLoad oversized = store.Load();
            True(oversized.RecoveredFromInvalid && oversized.Result is null,
                "Oversized benchmark state did not fail closed.");

            store.Save(expected);
            store.Clear();
            True(!File.Exists(path) && store.Load().Result is null,
                "Clearing the saved benchmark result left state behind.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void BenchmarkExecutionRequiresVisibleLab()
    {
        True(BenchmarkActivityPolicy.ShouldContinue(
                isLoaded: true,
                isVisible: true,
                isClosing: false,
                WindowState.Normal,
                monitoringVisible: true,
                benchmarkVisible: true),
            "The visible benchmark lab should admit an explicit run.");
        True(!BenchmarkActivityPolicy.ShouldContinue(
                isLoaded: true,
                isVisible: true,
                isClosing: false,
                WindowState.Normal,
                monitoringVisible: false,
                benchmarkVisible: true),
            "Navigating away must cancel the benchmark.");
        True(!BenchmarkActivityPolicy.ShouldContinue(
                isLoaded: true,
                isVisible: true,
                isClosing: false,
                WindowState.Minimized,
                monitoringVisible: true,
                benchmarkVisible: true),
            "Minimizing must cancel the benchmark.");
        True(!BenchmarkActivityPolicy.ShouldContinue(
                isLoaded: true,
                isVisible: false,
                isClosing: false,
                WindowState.Normal,
                monitoringVisible: true,
                benchmarkVisible: true),
            "Hiding to the notification area must cancel the benchmark.");
        True(!BenchmarkActivityPolicy.ShouldContinue(
                isLoaded: true,
                isVisible: true,
                isClosing: true,
                WindowState.Normal,
                monitoringVisible: true,
                benchmarkVisible: true),
            "Closing must cancel the benchmark.");
    }

    private static BenchmarkResult CreateBenchmarkResult()
    {
        BenchmarkMetric cpu = new(
            "cpu-sha256", "SHA-256 throughput", 1234, "MiB/s", TimeSpan.FromMilliseconds(1500), "1 MiB blocks");
        BenchmarkMetric memory = new(
            "memory-copy", "Buffer copy throughput", 5678, "MiB/s", TimeSpan.FromMilliseconds(1200), "8 MiB buffers");
        BenchmarkMetric write = new(
            "storage-write", "Temporary write", 400, "MiB/s", TimeSpan.FromMilliseconds(80), "32 MiB");
        BenchmarkMetric read = new(
            "storage-read", "Temporary read", 900, "MiB/s", TimeSpan.FromMilliseconds(36), "32 MiB");
        return new BenchmarkResult(
            BenchmarkProfile.Quick.Id,
            BenchmarkProfile.Quick.Version,
            DateTimeOffset.UtcNow.AddSeconds(-4),
            DateTimeOffset.UtcNow,
            16,
            8,
            cpu,
            memory,
            write,
            read,
            BenchmarkResultContract.Limitations);
    }

    private static void ApplicationsViewRenders(
        ApplicationInventorySnapshot snapshot,
        WindowsServiceInventorySnapshot serviceSnapshot)
    {
        ApplicationsView view = new();
        view.UpdateSnapshot(snapshot, serviceSnapshot);
        True(view.InventorySummaryText.Text.Contains("installed", StringComparison.Ordinal),
            "Applications view omitted the compact inventory summary.");
        True(ReferenceEquals(view.InventorySearchBox.Style, Application.Current.FindResource("FieldStyle")),
            "Applications search must use the shared field style.");
        True(view.SearchHintText.Visibility == Visibility.Visible,
            "Applications search hint must be visible when the field is empty.");
        view.InventorySearchBox.Text = "__soltex_no_inventory_match__";
        True(view.SearchHintText.Visibility == Visibility.Collapsed,
            "Applications search hint did not clear after input.");
        True(view.InventoryEmptyText.Visibility == Visibility.Visible,
            "Applications empty state did not appear for a query with no matches.");
        view.InventorySearchBox.Clear();
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The Applications view render was unexpectedly empty.");
        True(view.InstalledGrid.Items.Count == snapshot.Installed.Count,
            "Applications view lost installed rows.");
        True(view.StartupGrid.Visibility == Visibility.Collapsed,
            "Startup inventory must be quiet on first view.");
        view.StartupTabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.StartupGrid.Visibility == Visibility.Visible,
            "Startup inventory did not open from its explicit tab.");
        True(view.StartupGrid.Items.Count == snapshot.Startup.Count,
            "Applications view lost startup rows.");
        view.ServicesTabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.ServicesGrid.Visibility == Visibility.Visible,
            "Windows services did not open from their explicit tab.");
        True(view.ServicesGrid.Items.Count == serviceSnapshot.Services.Count,
            "Applications view lost service rows.");
        Visibility expectedAttention = serviceSnapshot.Services.Any(item =>
            !string.IsNullOrWhiteSpace(item.Signal))
            ? Visibility.Visible
            : Visibility.Collapsed;
        True(view.ServiceAttentionColumn.Visibility == expectedAttention,
            "The service attention column did not follow meaningful signal availability.");
    }

    private static void PreferencesRoundTripAndRecovery()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "soltex-preferences-tests-" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(directory, "preferences.json");
        Directory.CreateDirectory(directory);
        try
        {
            PreferencesStore store = new(filePath);
            SoltexPreferences expected = new(
                TelemetryCadence.Quiet,
                RestoreLastWorkspace: false,
                OpenPerformanceDetails: true,
                ActivityRetention: ActivityRetention.SevenDays,
                CloseBehavior: CloseBehavior.NotificationArea,
                PreferredPlaybackEndpointKey: new string('a', 64),
                PreferredRecordingEndpointKey: new string('b', 64),
                LastWorkspace: "security");
            store.Save(expected);
            PreferencesLoadResult loaded = store.Load();
            True(!loaded.RecoveredFromInvalid,
                "A valid preference document was treated as recovered.");
            True(loaded.Preferences == expected,
                "Preference round-trip changed a supported value.");

            File.WriteAllText(
                filePath,
                "{\"schemaVersion\":1,\"telemetryCadence\":\"Balanced\",\"restoreLastWorkspace\":true," +
                "\"openPerformanceDetails\":false,\"activityRetention\":\"SessionOnly\",\"lastWorkspace\":\"home\"}",
                Encoding.UTF8);
            PreferencesLoadResult migrated = store.Load();
            True(!migrated.RecoveredFromInvalid &&
                 migrated.Preferences.CloseBehavior == CloseBehavior.Exit,
                "Schema-one preferences did not migrate to the safe Exit behavior.");
            True(
                migrated.Preferences.PreferredPlaybackEndpointKey.Length == 0 &&
                migrated.Preferences.PreferredRecordingEndpointKey.Length == 0,
                "Legacy preferences did not migrate to empty audio fallback reminders.");

            File.WriteAllText(
                filePath,
                "{\"schemaVersion\":3,\"telemetryCadence\":\"Balanced\",\"restoreLastWorkspace\":true," +
                "\"openPerformanceDetails\":false,\"activityRetention\":\"SessionOnly\"," +
                "\"closeBehavior\":\"Exit\",\"preferredPlaybackEndpointKey\":\"not-an-endpoint-key\"," +
                "\"preferredRecordingEndpointKey\":\"\",\"lastWorkspace\":\"mixer\"}",
                Encoding.UTF8);
            PreferencesLoadResult invalidEndpointKey = store.Load();
            True(invalidEndpointKey.RecoveredFromInvalid,
                "A malformed audio fallback fingerprint was not reported as recovered.");
            True(
                invalidEndpointKey.Preferences.PreferredPlaybackEndpointKey.Length == 0 &&
                invalidEndpointKey.Preferences.LastWorkspace == "mixer",
                "Malformed audio fallback state did not recover only the unsupported value.");

            File.WriteAllText(filePath, "{ invalid", Encoding.UTF8);
            PreferencesLoadResult invalid = store.Load();
            True(invalid.RecoveredFromInvalid,
                "Invalid JSON did not fall back to defaults.");
            True(invalid.Preferences == SoltexPreferences.Default,
                "Invalid JSON did not recover the exact defaults.");

            File.WriteAllBytes(
                filePath,
                new byte[PreferencesStore.MaximumPreferenceBytes + 1]);
            PreferencesLoadResult oversized = store.Load();
            True(oversized.RecoveredFromInvalid,
                "An oversized preference document did not fail closed.");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private static void ActivityStoreBoundsAndRecovers()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "soltex-activity-tests-" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(directory, "activity.json");
        Directory.CreateDirectory(directory);
        try
        {
            LocalActivityStore store = new(filePath);
            ActivityLoadResult empty = store.Load(ActivityRetention.SessionOnly);
            True(empty.Entries.Count == 0,
                "Session-only Activity unexpectedly loaded saved history.");

            for (int index = 0; index < LocalActivityStore.MaximumEntryCount + 12; index++)
            {
                store.Add(
                    "System",
                    "Bounded event " + index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ActivityRetention.SessionOnly);
            }

            ActivityMutationResult pathLike = store.Add(
                "Security",
                @"Reviewed C:\Users\Person\private-item.exe",
                ActivityRetention.SessionOnly);
            True(pathLike.Entry is not null &&
                 !pathLike.Entry.Summary.Contains(@"C:\", StringComparison.OrdinalIgnoreCase),
                "Activity exposed path-like text.");
            ActivityMutationResult uncPathLike = store.Add(
                @"C:\Users\Person",
                @"Reviewed \\server\private\item.exe",
                ActivityRetention.SessionOnly);
            True(uncPathLike.Entry is not null &&
                 uncPathLike.Entry.Area == "System" &&
                 !uncPathLike.Entry.Summary.Contains(@"\\server", StringComparison.OrdinalIgnoreCase),
                "Activity exposed path-like text from an untrusted area or UNC summary.");
            True(store.Snapshot().Count == LocalActivityStore.MaximumEntryCount,
                "Activity did not enforce its entry bound.");
            True(!File.Exists(filePath),
                "Session-only Activity created a durable file.");

            ActivityMutationResult persisted = store.SetRetention(
                ActivityRetention.SevenDays,
                removePersistedWhenSessionOnly: false);
            True(persisted.StorageHealthy && File.Exists(filePath),
                "Activity did not persist after explicit retention.");

            LocalActivityStore reloadedStore = new(filePath);
            ActivityLoadResult reloaded =
                reloadedStore.Load(ActivityRetention.SevenDays);
            True(!reloaded.RecoveredFromInvalid &&
                 reloaded.Entries.Count == LocalActivityStore.MaximumEntryCount,
                "Valid bounded Activity did not round-trip.");

            ActivityMutationResult cleared = reloadedStore.Clear();
            True(cleared.StorageHealthy &&
                 reloadedStore.Snapshot().Count == 0 &&
                 !File.Exists(filePath),
                "Confirmed Activity clearing did not remove durable history.");

            File.WriteAllText(filePath, "{ invalid", Encoding.UTF8);
            ActivityLoadResult invalid =
                reloadedStore.Load(ActivityRetention.SevenDays);
            True(invalid.RecoveredFromInvalid && invalid.Entries.Count == 0,
                "Invalid Activity did not recover to an empty timeline.");

            File.WriteAllBytes(
                filePath,
                new byte[LocalActivityStore.MaximumFileBytes + 1]);
            ActivityLoadResult oversized =
                reloadedStore.Load(ActivityRetention.SevenDays);
            True(oversized.RecoveredFromInvalid && oversized.Entries.Count == 0,
                "Oversized Activity did not fail closed.");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private static void ActivityViewRenders()
    {
        ActivityView view = new();
        ActivityEntry[] entries =
        [
            ActivityEntry.Create(
                "Security",
                "Windows protection health refreshed.",
                DateTimeOffset.UtcNow),
            ActivityEntry.Create(
                "Performance",
                "Windows telemetry recovered after a bounded retry.",
                DateTimeOffset.UtcNow.AddMinutes(-2))
        ];
        view.UpdateEntries(
            entries,
            ActivityRetention.SevenDays,
            "Saved on this Windows account for up to 7 days.",
            storageHealthy: true);
        True(view.ActivityItems.Items.Count == 2,
            "Activity view did not render its events.");
        True((string)view.ActivityRetentionText.Text == "7 DAYS",
            "Activity view did not expose its retention state.");

        view.ActivitySearchBox.Text = "__no_activity_match__";
        True(view.ActivityItems.Visibility == Visibility.Collapsed &&
             view.ActivityEmptyPanel.Visibility == Visibility.Visible,
            "Activity search did not expose a clear no-match state.");
        view.ActivitySearchBox.Clear();

        bool clearRequested = false;
        view.ClearRequested += (_, _) => clearRequested = true;
        view.ClearActivityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(clearRequested,
            "Activity view did not route deletion through its confirmation owner.");

        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The Activity view render was unexpectedly empty.");
    }

    private static void SettingsViewRenders()
    {
        SettingsView view = new();
        SoltexPreferences preferences = new(
            TelemetryCadence.Quiet,
            RestoreLastWorkspace: false,
            OpenPerformanceDetails: true,
            ActivityRetention: ActivityRetention.ThirtyDays,
            CloseBehavior: CloseBehavior.Exit,
            PreferredPlaybackEndpointKey: string.Empty,
            PreferredRecordingEndpointKey: string.Empty,
            LastWorkspace: "monitoring");
        view.UpdateNotificationAreaAvailability(available: true);
        view.UpdatePreferences(
            preferences,
            recoveredFromInvalid: false,
            "Preferences loaded from this Windows account.");
        True((string)view.QuietCadenceButton.Content == "Quiet · 5 s",
            "Settings omitted the quiet cadence option.");
        True((string)view.PerformanceDetailsButton.Content == "On",
            "Settings did not render the Performance detail preference.");
        True((string)view.RestoreWorkspaceButton.Content == "Off",
            "Settings did not render the workspace restore preference.");
        True((string)view.ThirtyDayActivityButton.Content == "30 days",
            "Settings omitted the explicit Activity retention option.");
        True(view.ExitOnCloseButton.Foreground == Application.Current.FindResource("AccentBrush"),
            "Settings did not render Exit as the default close behavior.");
        True(view.GeneralSettingsPanel.Visibility == Visibility.Visible &&
             view.WindowSettingsPanel.Visibility == Visibility.Collapsed,
            "Settings did not open with one bounded category.");
        view.WindowSettingsTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.GeneralSettingsPanel.Visibility == Visibility.Collapsed &&
             view.WindowSettingsPanel.Visibility == Visibility.Visible,
            "Settings category switch did not replace the visible work area.");
        view.PrivacySettingsTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.WindowSettingsPanel.Visibility == Visibility.Collapsed &&
             view.PrivacySettingsPanel.Visibility == Visibility.Visible,
            "Settings activity category did not replace the window category.");

        SoltexPreferences? changed = null;
        view.PreferencesChanged += (_, args) => changed = args.Preferences;
        view.LiveCadenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(changed?.TelemetryCadence == TelemetryCadence.Live,
            "Settings did not emit the selected cadence.");
        view.NotificationAreaButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(changed?.CloseBehavior == CloseBehavior.NotificationArea,
            "Settings did not emit the explicit notification-area behavior.");
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The Settings view render was unexpectedly empty.");
    }

    private static void MixerPrioritizesActiveEndpoints(
        AudioEndpointSnapshot snapshot,
        AudioSessionSnapshot sessionSnapshot)
    {
        MixerView view = new();
        view.UpdateSnapshot(snapshot, sessionSnapshot);
        int activePlayback = snapshot.Render.Count(endpoint => endpoint.State == AudioEndpointState.Active);
        int activeRecording = snapshot.Capture.Count(endpoint => endpoint.State == AudioEndpointState.Active);
        int expectedPrimary = Math.Min(activePlayback, 6) + Math.Min(activeRecording, 6);
        int moreCount = snapshot.Endpoints.Count - expectedPrimary;
        True(view.PlaybackItems.Items.Count + view.RecordingItems.Items.Count == expectedPrimary,
            "Mixer did not keep its primary endpoint lists bounded and active-only.");
        True(expectedPrimary <= 12, "Mixer exposed more than twelve endpoints in the primary view.");
        True(view.MorePlaybackItems.Items.Count + view.MoreRecordingItems.Items.Count == moreCount,
            "Mixer lost endpoints while partitioning the primary and additional lists.");
        True(view.EndpointDetailsPanel.Visibility == Visibility.Collapsed,
            "Audio device lists must be collapsed on first view.");
        True(view.MixerOverviewPanel.Visibility == Visibility.Visible,
            "Audio mixer must be visible on first view.");
        view.DeviceDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.EndpointDetailsPanel.Visibility == Visibility.Visible,
            "Audio device lists did not open from their disclosure control.");
        True(view.MixerOverviewPanel.Visibility == Visibility.Collapsed,
            "Audio mixer remained visible behind device details.");
        True(view.MoreEndpointsPanel.Visibility == Visibility.Collapsed,
            "Additional audio endpoints must be collapsed on first view.");
        if (moreCount > 0)
        {
            True(view.MoreEndpointsButton.Visibility == Visibility.Visible,
                "Mixer omitted the additional-endpoint disclosure control.");
            view.MoreEndpointsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            True(view.MoreEndpointsPanel.Visibility == Visibility.Visible,
                "Mixer additional endpoints did not open from their disclosure control.");
        }


        True(view.SessionItems.Items.Count == 5,
            "Mixer did not keep the primary app-session list at five rows.");
        True(view.MoreSessionItems.Items.Count == 2,
            "Mixer lost app sessions while partitioning its primary and additional lists.");
        True(view.MoreSessionItems.Visibility == Visibility.Collapsed,
            "Additional app sessions must be collapsed on first view.");
        view.MoreSessionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.MoreSessionItems.Visibility == Visibility.Visible,
            "Mixer additional app sessions did not open from their disclosure control.");
        True(view.SessionSummaryText.Text.Contains("7 active", StringComparison.Ordinal),
            "Mixer omitted its bounded active-session summary.");
        True(view.CaptureMixSnapshotButton.IsEnabled &&
             !view.ApplyMixSnapshotButton.IsEnabled &&
             !view.ClearMixSnapshotButton.IsEnabled,
            "Mixer did not make capture the primary available action without a saved snapshot.");
        AudioMixSnapshot savedMix = new(
            DateTimeOffset.UtcNow,
            [new AudioMixEntry("Audio app 1", "Speakers", 30, false)]);
        view.UpdateMixSnapshot(new AudioMixLoadResult(
            savedMix,
            RecoveredFromInvalid: false,
            AudioMixSnapshotStore.Describe(savedMix)));
        True(view.ApplyMixSnapshotButton.IsEnabled && view.ClearMixSnapshotButton.IsEnabled,
            "Mixer did not enable recall and clear after loading a valid snapshot.");
        True(ReferenceEquals(
                view.ApplyMixSnapshotButton.Style,
                Application.Current.FindResource("ActionButton")),
            "Mixer did not promote recall to the primary action after a snapshot was saved.");

        bool refreshRequested = false;
        view.RefreshRequested += (_, _) => refreshRequested = true;
        view.RefreshAudioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(refreshRequested, "Mixer refresh did not emit a refresh request.");
        True(!view.RefreshAudioButton.IsEnabled,
            "Mixer controls stayed enabled while an observation was pending.");

        AudioSession selected = sessionSnapshot.Sessions[0];
        AudioSessionMutationResult verified = new(
            AudioSessionMutationKind.Volume,
            AudioSessionMutationStatus.Applied,
            selected.Name,
            46,
            null,
            46,
            false,
            $"{selected.Name} volume is 46% (verified).");
        view.ShowSessionMutationResult(verified);
        True(view.RefreshAudioButton.IsEnabled,
            "Mixer controls did not recover after the mutation result.");
        True(view.SessionActionStateText.Text == "VERIFIED",
            "Mixer did not distinguish verified read-back success.");
        True(view.SessionActionDetailText.Text.Contains("verified", StringComparison.OrdinalIgnoreCase),
            "Mixer did not expose the read-back result.");

        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The session-enabled Mixer view render was unexpectedly empty.");
    }

    private static void MixerFallbackRemindersAreBounded()
    {
        string playbackDefaultKey = new('a', 64);
        string playbackFallbackKey = new('b', 64);
        string recordingKey = new('c', 64);
        AudioEndpoint[] endpoints =
        [
            new("Speakers", AudioEndpointDirection.Render, AudioEndpointState.Active,
                IsDefault: true, VolumeScalar: 0.42, IsMuted: false,
                PreferenceKey: playbackDefaultKey),
            new("Headset", AudioEndpointDirection.Render, AudioEndpointState.Active,
                IsDefault: false, VolumeScalar: 0.35, IsMuted: false,
                PreferenceKey: playbackFallbackKey),
            new("Microphone", AudioEndpointDirection.Capture, AudioEndpointState.Active,
                IsDefault: true, VolumeScalar: 0.60, IsMuted: false,
                PreferenceKey: recordingKey)
        ];
        AudioEndpointSnapshot endpointSnapshot = new(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(2),
            AudioObservationState.Current,
            endpoints,
            inaccessibleEndpointCount: 0,
            provenance: "IMMDeviceEnumerator",
            limitations: ["System default assignment remains Windows-owned."]);
        MixerView view = new();
        view.UpdateEndpointPreferences(string.Empty, string.Empty);
        view.UpdateSnapshot(endpointSnapshot, CreateAudioSessionSnapshot());
        True(!view.ClearEndpointPreferencesButton.IsEnabled,
            "Mixer enabled clearing when no fallback reminder was saved.");
        True(view.EndpointPreferenceStateText.Text == "OPTIONAL",
            "Mixer implied that an unset fallback reminder was active.");

        AudioEndpoint? requested = null;
        view.EndpointPreferenceRequested += (_, args) => requested = args.Endpoint;
        view.RequestEndpointPreference(endpoints[1]);
        True(ReferenceEquals(requested, endpoints[1]),
            "Mixer did not emit the explicitly selected fallback endpoint.");
        True(view.EndpointPreferenceStateText.Text == "SAVING",
            "Mixer presented an unpersisted fallback reminder as saved.");

        view.UpdateEndpointPreferences(playbackFallbackKey, recordingKey);
        view.ShowEndpointPreferenceResult(
            saved: true,
            "Fallback reminders are saved on this Windows account.");
        True(view.ClearEndpointPreferencesButton.IsEnabled,
            "Mixer did not expose clearing after fallback reminders were saved.");
        True(view.EndpointPreferenceStateText.Text == "REMEMBERED",
            "Mixer did not distinguish a persisted fallback reminder.");

        requested = null;
        view.RequestEndpointPreference(endpoints[1]);
        True(requested is null,
            "Mixer re-emitted an already saved fallback reminder.");

        AudioSessionSnapshot unavailableSessions = new(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1),
            AudioObservationState.Unavailable,
            Array.Empty<AudioSession>(),
            observedSessionCount: 0,
            inaccessibleSessionCount: 1,
            omittedSessionCount: 0,
            provenance: "IAudioSessionManager2",
            limitations: ["Session observation unavailable."]);
        view.UpdateSnapshot(endpointSnapshot, unavailableSessions);
        True(view.MixerStateText.Text == "PARTIAL",
            "Mixer did not preserve partial truth when endpoint observation outlived session observation.");
        view.UpdateEndpointPreferences(playbackFallbackKey, recordingKey);
        True(view.MixerStateText.Text == "PARTIAL",
            "Refreshing fallback reminder state overwrote the merged audio observation state.");
    }

    private static void WindowsSoundHandoffIsFixed()
    {
        WindowsSoundSettingsLaunchPlan plan = WindowsSoundSettingsLauncher.CreatePlan();
        True(plan.FileName == "ms-settings:sound",
            "Windows Sound handoff changed away from the fixed supported URI.");
        True(plan.UseShellExecute && !plan.ErrorDialog,
            "Windows Sound handoff did not use the bounded URI launch plan.");

        FakeWindowsSoundSettingsLaunchBackend backend = new();
        WindowsSoundSettingsLaunchResult result = WindowsSoundSettingsLauncher.Open(backend);
        True(result.Started, "The fixed Windows Sound handoff did not report launch dispatch.");
        True(backend.Plan == plan,
            "The Windows Sound launcher dispatched a plan other than the reviewed fixed plan.");
    }

    private static void AudioMixSnapshotIsBoundedAndRecoverable()
    {
        string root = Path.Combine(Path.GetTempPath(), "soltex-audio-mix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "audio-mix.json");
            AudioMixSnapshotStore store = new(path);
            AudioSession one = new(
                "Game",
                "Headset",
                0.42,
                isMuted: false,
                canControl: true,
                "Volume and mute available",
                new AudioSessionIdentity("endpoint-one", "session-one", 101, 1001));
            AudioSession duplicate = new(
                "Game",
                "Headset",
                0.64,
                isMuted: true,
                canControl: true,
                "Volume and mute available",
                new AudioSessionIdentity("endpoint-two", "session-two", 102, 1002));
            AudioSession chat = new(
                "Chat",
                "Headset",
                0.75,
                isMuted: true,
                canControl: true,
                "Volume and mute available",
                new AudioSessionIdentity("endpoint-three", "session-three", 103, 1003));
            AudioSession unavailable = new(
                "System sounds",
                "Headset",
                0.90,
                isMuted: false,
                canControl: false,
                "Control unavailable",
                null);

            AudioMixCaptureResult capture = AudioMixSnapshotPlanner.Capture(
                [one, duplicate, chat, unavailable]);
            True(capture.Snapshot is { Entries.Count: 1 },
                "Capture did not omit duplicate or uncontrollable sessions.");
            AudioMixSnapshot capturedSnapshot = capture.Snapshot!;
            True(capture.SkippedAmbiguous == 2 && capture.SkippedUncontrollable == 1,
                "Capture omission counts were not explicit.");
            True(capturedSnapshot.Entries[0].ApplicationName == "Chat" &&
                 capturedSnapshot.Entries[0].VolumePercent == 75 &&
                 capturedSnapshot.Entries[0].IsMuted,
                "Capture did not preserve the unique controllable session state.");

            store.Save(capturedSnapshot);
            string persisted = File.ReadAllText(path);
            True(!persisted.Contains("endpoint-three", StringComparison.Ordinal) &&
                 !persisted.Contains("session-three", StringComparison.Ordinal),
                "The snapshot persisted a raw Core Audio identity.");
            AudioMixLoadResult loaded = store.Load();
            True(!loaded.RecoveredFromInvalid && loaded.Snapshot is { Entries.Count: 1 },
                "The bounded mix snapshot did not round-trip.");

            AudioMixApplyPlan exact = AudioMixSnapshotPlanner.Plan(
                loaded.Snapshot!,
                [chat]);
            True(exact.Matches.Count == 1 && exact.MissingCount == 0 && exact.AmbiguousCount == 0,
                "Exact live session matching did not produce one apply target.");
            AudioMixApplyPlan changed = AudioMixSnapshotPlanner.Plan(
                loaded.Snapshot!,
                [one, duplicate]);
            True(changed.Matches.Count == 0 && changed.MissingCount == 1,
                "A changed app/endpoint pair was not reported missing.");

            File.WriteAllText(path, "{invalid-json");
            AudioMixLoadResult recovered = store.Load();
            True(recovered.RecoveredFromInvalid && recovered.Snapshot is null,
                "Malformed mix state did not fail closed.");

            File.WriteAllText(path, new string('x', AudioMixSnapshotStore.MaximumDocumentBytes + 1));
            AudioMixLoadResult oversized = store.Load();
            True(oversized.RecoveredFromInvalid && oversized.Snapshot is null,
                "Oversized mix state did not fail closed.");

            store.Save(capturedSnapshot);
            store.Clear();
            True(!File.Exists(path) && store.Load().Snapshot is null,
                "Clearing the mix snapshot left persisted state behind.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AudioSessionSnapshot CreateAudioSessionSnapshot()
    {
        AudioSession[] sessions = Enumerable.Range(1, 7)
            .Select(index => new AudioSession(
                $"Audio app {index}",
                index % 2 == 0 ? "Headset" : "Speakers",
                0.25 + (index * 0.05),
                isMuted: index == 3,
                canControl: index != 7,
                index == 7 ? "Multi-process or transferred session" : "Volume and mute available",
                index == 7
                    ? null
                    : new AudioSessionIdentity($"endpoint-{index}", $"session-{index}", (uint)(100 + index), 1000 + index)))
            .ToArray();
        return new AudioSessionSnapshot(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(8),
            AudioObservationState.Current,
            sessions,
            observedSessionCount: 7,
            inaccessibleSessionCount: 0,
            omittedSessionCount: 0,
            "IMMDeviceEnumerator · IAudioSessionManager2 · IAudioSessionControl2 · ISimpleAudioVolume",
            ["Test fixture: routing and processing are not provided."]);
    }

    private static void DevicesViewRenders(LocalDeviceObservation device)
    {
        DevicesView view = new();
        view.UpdateObservation(device);
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000, "The Devices view render was unexpectedly empty.");
        True(view.CapabilityItems.Items.Count == 6, "Devices did not render the exact capability catalog.");
        True(view.DeviceProvenanceText.Text.Contains("NotEnrolled", StringComparison.Ordinal), "Devices did not expose the unenrolled state.");
        True(view.DeviceHeroCard.ActualHeight <= 221, "The Devices hero exceeded its bounded viewport height.");
        True(view.DeviceProfileCard.ActualWidth >= 240, "The Devices profile collapsed below its usable width.");
    }

    private static void RenderSmokeUsesCanonicalViewport()
    {
        Grid surface = new()
        {
            Background = Brushes.Black
        };
        TextBlock inheritedText = new()
        {
            Text = "Inherited foreground",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        surface.Children.Add(inheritedText);
        surface.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Magenta
        });
        Window constrainedHost = new()
        {
            Width = 1044,
            Height = 788,
            Foreground = Brushes.White,
            Content = surface
        };
        RenderSmokeCapture.ConfigureWindow(constrainedHost);
        constrainedHost.ShowInTaskbar = false;
        constrainedHost.WindowStartupLocation = WindowStartupLocation.Manual;
        constrainedHost.Left = -32_000;
        constrainedHost.Top = -32_000;
        constrainedHost.Show();

        RenderTargetBitmap bitmap;
        try
        {
            bitmap = RenderSmokeCapture.Capture(constrainedHost);
        }
        finally
        {
            constrainedHost.Close();
        }
        True(ReferenceEquals(constrainedHost.Content, surface),
            "Render-smoke replaced the product content surface.");
        True(constrainedHost.WindowStyle == WindowStyle.None && constrainedHost.ResizeMode == ResizeMode.NoResize,
            "Render-smoke did not configure a borderless, nonresizable popup window.");
        True(ReferenceEquals(TextElement.GetForeground(inheritedText), Brushes.White),
            "Render-smoke lost a descendant foreground inherited from the constrained host.");
        True(bitmap.PixelWidth == 1280 && bitmap.PixelHeight == 820,
            "Render-smoke did not use the canonical 1280x820 viewport.");

        byte[] pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, checked(bitmap.PixelWidth * 4), 0);
        int bottomRight = checked((((bitmap.PixelHeight - 8) * bitmap.PixelWidth) + bitmap.PixelWidth - 8) * 4);
        True(pixels[bottomRight] > 200 && pixels[bottomRight + 2] > 200 && pixels[bottomRight + 3] > 200,
            "Render-smoke clipped content that expanded beyond the constrained host viewport.");
    }

    private static byte[] Render(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        byte[] pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, checked(width * 4), 0);
        return pixels;
    }

    private static int CountVisiblePixels(byte[] pixels)
    {
        int visible = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 0)
            {
                visible++;
            }
        }

        return visible;
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Near(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {expected}, observed {actual} (tolerance {tolerance}).");
        }
    }
}

internal sealed class FakeWindowsSoundSettingsLaunchBackend : IWindowsSoundSettingsLaunchBackend
{
    internal WindowsSoundSettingsLaunchPlan? Plan { get; private set; }

    public void Launch(WindowsSoundSettingsLaunchPlan plan) => Plan = plan;
}
