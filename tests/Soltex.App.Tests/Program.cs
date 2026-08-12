using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Soltex.App;
using Soltex.App.Controls;
using Soltex.App.Views;
using Soltex.Audio;
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
        ApplicationInventorySnapshot applicationSnapshot =
            ApplicationInventoryProvider.CaptureAsync().GetAwaiter().GetResult();

        List<(string Name, Action Test)> tests =
        [
            ("Shared theme exposes required control resources", ThemeResourcesAreAvailable),
            ("Telemetry runs only in visible live workspaces", TelemetryRunsOnlyInLiveWorkspaces),
            ("Process actions reject Windows and Soltex targets", ProcessActionsRejectProtectedTargets),
            ("Process actions reject identity drift", ProcessActionsRejectIdentityDrift),
            ("Process actions admit only the selected user-session process", ProcessActionsAdmitBoundedTarget),
            ("Sparkline renders bounded percentage values", SparklineRenders),
            ("Sparkline auto-scales unbounded throughput values", SparklineAutoScales),
            ("Home view renders a live snapshot", () => HomeViewRenders(snapshot, device)),
            ("Monitoring view renders provenance and bounded rows", () => MonitoringViewRenders(snapshot)),
            ("Monitoring details are disclosed only on request", MonitoringDetailsAreProgressive),
            ("Application inventory is bounded and path-free", () => ApplicationInventoryIsBounded(applicationSnapshot)),
            ("Applications view renders installed and startup tabs", () => ApplicationsViewRenders(applicationSnapshot)),
            ("Preference store recovers and round-trips bounded local state", PreferencesRoundTripAndRecovery),
            ("Settings view renders working local preferences", SettingsViewRenders),
            ("Mixer prioritizes active endpoints", () => MixerPrioritizesActiveEndpoints(audioSnapshot)),
            ("Devices view renders an explicit unnrolled profile", () => DevicesViewRenders(device)),
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
            isClosing: false,
            WindowState.Normal,
            homeVisible: true,
            monitoringVisible: false),
            "Home should keep telemetry active.");
        True(TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isClosing: false,
            WindowState.Maximized,
            homeVisible: false,
            monitoringVisible: true),
            "Monitoring should keep telemetry active.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isClosing: false,
            WindowState.Normal,
            homeVisible: false,
            monitoringVisible: false),
            "Hidden live workspaces must suspend telemetry.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isClosing: false,
            WindowState.Minimized,
            homeVisible: true,
            monitoringVisible: false),
            "A minimized window must suspend telemetry.");
        True(!TelemetryActivityPolicy.ShouldRun(
            isLoaded: true,
            isClosing: true,
            WindowState.Normal,
            homeVisible: true,
            monitoringVisible: false),
            "Closing must prevent telemetry restart.");
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
        True(view.MonitoringDetailsPanel.Visibility == Visibility.Collapsed,
            "Monitoring detail must be collapsed on first view.");
        view.MonitoringDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(view.MonitoringDetailsPanel.Visibility == Visibility.Visible,
            "Monitoring detail did not open from its explicit disclosure control.");
        view.MonitoringDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    private static void ApplicationsViewRenders(ApplicationInventorySnapshot snapshot)
    {
        ApplicationsView view = new();
        view.UpdateSnapshot(snapshot);
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
                LastWorkspace: "security");
            store.Save(expected);
            PreferencesLoadResult loaded = store.Load();
            True(!loaded.RecoveredFromInvalid,
                "A valid preference document was treated as recovered.");
            True(loaded.Preferences == expected,
                "Preference round-trip changed a supported value.");

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

    private static void SettingsViewRenders()
    {
        SettingsView view = new();
        SoltexPreferences preferences = new(
            TelemetryCadence.Quiet,
            RestoreLastWorkspace: false,
            OpenPerformanceDetails: true,
            LastWorkspace: "monitoring");
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

        SoltexPreferences? changed = null;
        view.PreferencesChanged += (_, args) => changed = args.Preferences;
        view.LiveCadenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        True(changed?.TelemetryCadence == TelemetryCadence.Live,
            "Settings did not emit the selected cadence.");
        byte[] pixels = Render(view, 980, 720);
        True(CountVisiblePixels(pixels) > 5_000,
            "The Settings view render was unexpectedly empty.");
    }

    private static void MixerPrioritizesActiveEndpoints(AudioEndpointSnapshot snapshot)
    {
        MixerView view = new();
        view.UpdateSnapshot(snapshot);
        int activePlayback = snapshot.Render.Count(endpoint => endpoint.State == AudioEndpointState.Active);
        int activeRecording = snapshot.Capture.Count(endpoint => endpoint.State == AudioEndpointState.Active);
        int expectedPrimary = Math.Min(activePlayback, 6) + Math.Min(activeRecording, 6);
        int moreCount = snapshot.Endpoints.Count - expectedPrimary;
        True(view.PlaybackItems.Items.Count + view.RecordingItems.Items.Count == expectedPrimary,
            "Mixer did not keep its primary endpoint lists bounded and active-only.");
        True(expectedPrimary <= 12, "Mixer exposed more than twelve endpoints in the primary view.");
        True(view.MorePlaybackItems.Items.Count + view.MoreRecordingItems.Items.Count == moreCount,
            "Mixer lost endpoints while partitioning the primary and additional lists.");
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
}
