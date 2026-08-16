using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Soltex.App.Views;
using Soltex.Audio;
using Soltex.DeviceFabric;
using Soltex.Monitoring;
using Soltex.RemoteAssist;
using Soltex.Security;
using Soltex.Update;
using Soltex.Whisper;

namespace Soltex.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Window lifecycle; Closed cancels and disposes every owned resource.")]
public partial class MainWindow : Window
{
    private FrameworkElement? _renderSmokeFocusTarget;
    private readonly SecurityRuntime _runtime = SecurityRuntime.CreateDefault();
    private readonly ObservableCollection<QuarantineRow> _quarantineRows = [];
    private readonly ObservableCollection<DefenderEventRow> _defenderEventRows = [];
    private readonly ObservableCollection<UpdateJournalRow> _updateJournalRows = [];
    private readonly UpdatePlanningJournal _updateJournal;
    private readonly string _updateStagingRoot;
    private readonly LocalDeviceObservation _localDevice = LocalDeviceObservationProvider.Capture();
    private CancellationTokenSource? _operationCancellation;
    private Task _activeOperationDrained = Task.CompletedTask;
    private readonly TelemetryLoopOwner _telemetryLoop = new();
    private Task? _startupTask;
    private ImportFolderMonitor? _importMonitor;
    private ProtectionMonitor? _protectionMonitor;
    private ProtectionMonitorState? _lastMonitorState;
    private WhisperOverlayWindow? _whisperOverlay;
    private Action? _whisperOverlayAction;
    private RemoteAssistExecutable? _remoteAssistExecutable;
    private AuthenticodeVerificationResult? _remoteAssistTrust;
    private bool _securityActivityVisible;
    private readonly PreferencesStore _preferencesStore;
    private readonly LocalActivityStore _activityStore;
    private readonly AudioMixSnapshotStore _audioMixSnapshotStore;
    private readonly BenchmarkResultStore _benchmarkResultStore;
    private readonly SemaphoreSlim _applicationRefreshGate = new(1, 1);
    private readonly TaskCompletionSource<bool> _startupCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private SoltexPreferences _preferences = SoltexPreferences.Default;
    private int _telemetryIntervalMilliseconds = SoltexPreferences.Default.TelemetryIntervalMilliseconds;
    private string _activeWorkspace = "home";
    private bool _shutdownStarted;
    private readonly bool _renderSmokeMode = RuntimeLaunchPolicy.UsesControlledRuntime(
        Environment.GetCommandLineArgs());

    internal bool NotificationAreaAvailable { get; private set; }

    internal bool KeepsRunningInNotificationArea =>
        _preferences.CloseBehavior == CloseBehavior.NotificationArea;

    internal event EventHandler? CloseBehaviorChanged;

    internal event EventHandler<ShutdownCompletedEventArgs>? ShutdownCompleted;

    internal Task StartupCompleted => _startupCompleted.Task;

    public MainWindow()
    {
        InitializeComponent();
        _preferencesStore = new PreferencesStore(
            Path.Combine(_runtime.DataRoot, "preferences.json"));
        PreferencesLoadResult preferencesLoad = _preferencesStore.Load();
        _preferences = preferencesLoad.Preferences;
        if (_renderSmokeMode)
        {
            _preferences = _preferences with
            {
                RestoreLastWorkspace = false,
                ActivityRetention = ActivityRetention.SessionOnly,
                CloseBehavior = CloseBehavior.Exit,
                PreferredPlaybackEndpointKey = string.Empty,
                PreferredRecordingEndpointKey = string.Empty
            };
        }
        Volatile.Write(
            ref _telemetryIntervalMilliseconds,
            _preferences.TelemetryIntervalMilliseconds);
        SettingsPanel.UpdatePreferences(
            _preferences,
            preferencesLoad.RecoveredFromInvalid,
            preferencesLoad.Detail);
        MixerPanel.UpdateEndpointPreferences(
            _preferences.PreferredPlaybackEndpointKey,
            _preferences.PreferredRecordingEndpointKey);
        NotificationAreaAvailable = OperatingSystem.IsWindows();
        SettingsPanel.UpdateNotificationAreaAvailability(NotificationAreaAvailable);
        _activityStore = new LocalActivityStore(
            Path.Combine(_runtime.DataRoot, "activity.json"));
        ActivityLoadResult activityLoad =
            _activityStore.Load(_preferences.ActivityRetention);
        ActivityPanel.UpdateEntries(
            activityLoad.Entries,
            _preferences.ActivityRetention,
            activityLoad.Detail,
            storageHealthy: !activityLoad.RecoveredFromInvalid);
        _audioMixSnapshotStore = new AudioMixSnapshotStore(
            Path.Combine(_runtime.DataRoot, "audio-mix.json"));
        MixerPanel.UpdateMixSnapshot(_audioMixSnapshotStore.Load());
        _benchmarkResultStore = new BenchmarkResultStore(
            Path.Combine(_runtime.DataRoot, "benchmark-result.json"));
        MonitoringPanel.UpdateBenchmarkLoad(_benchmarkResultStore.Load());
        MonitoringPanel.SetDetailsVisible(_preferences.OpenPerformanceDetails);
        InitializeWhisperCapture();
        _updateStagingRoot = Path.Combine(_runtime.DataRoot, "update", "staging");
        _updateJournal = new UpdatePlanningJournal(Path.Combine(_runtime.DataRoot, "update", "journal"));
        QuarantineGrid.ItemsSource = _quarantineRows;
        DefenderEventGrid.ItemsSource = _defenderEventRows;
        UpdateJournalGrid.ItemsSource = _updateJournalRows;
        CurrentBuildText.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "development";
        ShellBuildText.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "development";
        RemotePeerIdInput.TextChanged += RemotePeerId_TextChanged;
        SetRemoteAssistExecutable(RemoteAssistExecutableLocator.FindInstalled());
        DevicesPanel.UpdateObservation(_localDevice);
        DevicesPanel.RemoteAssistRequested += (_, _) => ShowPanel(RemotePanel, RemoteNavButton);
        WhisperPanel.PreviewOverlayRequested += (_, _) => ShowWhisperOverlayPreview();
        MonitoringPanel.ProcessActionCompleted += (_, args) =>
            AddActivity(args.Result.Message, "Performance");
        MonitoringPanel.BenchmarkRunRequested += MonitoringPanel_BenchmarkRunRequested;
        MonitoringPanel.BenchmarkCancelRequested += MonitoringPanel_BenchmarkCancelRequested;
        MonitoringPanel.BenchmarkClearRequested += MonitoringPanel_BenchmarkClearRequested;
        MonitoringPanel.BenchmarkModeChanged += MonitoringPanel_BenchmarkModeChanged;
        ApplicationsPanel.RefreshRequested += ApplicationsPanel_RefreshRequested;
        MixerPanel.RefreshRequested += MixerPanel_RefreshRequested;
        MixerPanel.SessionChangeRequested += MixerPanel_SessionChangeRequested;
        MixerPanel.EndpointPreferenceRequested += MixerPanel_EndpointPreferenceRequested;
        MixerPanel.ClearEndpointPreferencesRequested += MixerPanel_ClearEndpointPreferencesRequested;
        MixerPanel.OpenSoundSettingsRequested += MixerPanel_OpenSoundSettingsRequested;
        MixerPanel.CaptureMixSnapshotRequested += MixerPanel_CaptureMixSnapshotRequested;
        MixerPanel.ApplyMixSnapshotRequested += MixerPanel_ApplyMixSnapshotRequested;
        MixerPanel.ClearMixSnapshotRequested += MixerPanel_ClearMixSnapshotRequested;
        SettingsPanel.PreferencesChanged += SettingsPanel_PreferencesChanged;
        ActivityPanel.ClearRequested += ActivityPanel_ClearRequested;
    }

    internal void SetNotificationAreaAvailability(bool available)
    {
        NotificationAreaAvailable = available;
        SettingsPanel.UpdateNotificationAreaAvailability(available);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _startupTask ??= InitializeWorkspaceAsync();
        try
        {
            await _startupTask;
        }
        catch (OperationCanceledException) when (_shutdownStarted)
        {
            _startupCompleted.TrySetCanceled();
        }
        catch (Exception) when (_shutdownStarted)
        {
            _startupCompleted.TrySetCanceled();
        }
        catch (Exception exception)
        {
            _startupCompleted.TrySetException(exception);
            if (!_renderSmokeMode)
            {
                throw;
            }
        }
    }

    private async Task InitializeWorkspaceAsync()
    {
        if (!_renderSmokeMode && _preferences.RestoreLastWorkspace)
        {
            RestoreWorkspace(_preferences.LastWorkspace);
        }

        await _telemetryLoop.StartAsync(RunTelemetryLoopAsync);

        Task audioRefresh = RefreshAudioAsync();
        Task applicationRefresh = RefreshApplicationsAsync();
        Task whisperCaptureRefresh = CoreAudioStartupCoordinator.RunWhisperAfterAudioAsync(
            audioRefresh,
            RefreshWhisperCaptureDevicesAsync);
        _whisperShortcutDrained = ReconcileWhisperShortcutsAsync(
            _whisperRuntimeCancellation.Token);

        _importMonitor = new ImportFolderMonitor(
            _runtime.ImportsPath,
            _runtime.Assessor,
            HandleImportAssessmentAsync);
        _protectionMonitor = new ProtectionMonitor(
            _runtime.Defender,
            new WindowsSecurityChangeMonitor());
        _protectionMonitor.Updated += OnProtectionMonitorUpdated;
        await Task.WhenAll(
            RefreshAllAsync(),
            RefreshUpdateJournalAsync(),
            audioRefresh,
            applicationRefresh,
            whisperCaptureRefresh,
            _whisperShortcutDrained);
        if (_shutdownStarted)
        {
            _startupCompleted.TrySetCanceled();
            return;
        }

        _protectionMonitor.Start();
        AddActivity("Soltex import guard is active.", "Security");
        if (_runtime.DataRootKind == ProductDataRootKind.LegacyCompatibility)
        {
            AddActivity(
                "Soltex is using the existing compatible data location; no files were moved.",
                "Security");
        }

        AddActivity(
            _protectionMonitor.ChangeNotificationsAvailable
                ? "Windows Security change notifications are active."
                : "Windows Security notifications are unavailable; bounded polling remains active.",
            "Security");
        _startupCompleted.TrySetResult(true);
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _shutdownStarted = true;
        if (!_renderSmokeMode)
        {
            SavePreferencesForClose();
        }

        // The overlay is a separate top-level window, so it has to be closed
        // explicitly or it keeps the process alive after the shell is gone.
        _whisperOverlay?.Close();
        _whisperOverlay = null;

        _operationCancellation?.Cancel();
        _audioMixCancellation?.Cancel();
        _benchmarkCancellation?.Cancel();
        _whisperCaptureCancellation?.Cancel();
        _whisperRuntimeCancellation.Cancel();
        _whisperCapture?.CompleteCurrentCapture();
        await _telemetryLoop.StopAsync();
        bool ownedWorkDrained = await OwnedTaskDrain.WaitAsync(
            TimeSpan.FromSeconds(20),
            _activeOperationDrained,
            _audioMixOperationDrained,
            _benchmarkOperationDrained,
            _whisperCaptureDrained,
            _whisperShortcutDrained,
            _startupTask);
        if (!ownedWorkDrained)
        {
            // Do not dispose state that an in-flight task may still reference.
            // The owning App will treat an evidence-mode cleanup timeout as a
            // failed run; normal application shutdown is already in progress.
            ShutdownCompleted?.Invoke(
                this,
                new ShutdownCompletedEventArgs(resourcesDisposed: false));
            return;
        }

        if (_importMonitor is not null)
        {
            await _importMonitor.DisposeAsync();
        }

        if (_protectionMonitor is not null)
        {
            _protectionMonitor.Updated -= OnProtectionMonitorUpdated;
            await _protectionMonitor.DisposeAsync();
        }

        await DisposeWhisperRuntimeAsync();
        await DisposeWhisperCaptureAsync();

        _updateJournal.Dispose();
        _runtime.Dispose();
        ShutdownCompleted?.Invoke(
            this,
            new ShutdownCompletedEventArgs(resourcesDisposed: true));
    }

    private async Task RunTelemetryLoopAsync(CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        bool recoveryNoticeRequired = false;
        bool hasSuccessfulSample = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SystemTelemetrySnapshot snapshot = await SystemTelemetryProvider.CaptureAsync(
                    TimeSpan.FromMilliseconds(300),
                    cancellationToken).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    HomePanel.UpdateSnapshot(snapshot, _localDevice);
                    MonitoringPanel.UpdateSnapshot(snapshot);
                    DevicesPanel.UpdateObservation(_localDevice);
                    if (recoveryNoticeRequired)
                    {
                        AddActivity(
                            "Windows telemetry recovered after a bounded retry.",
                            "Performance");
                    }
                });
                consecutiveFailures = 0;
                recoveryNoticeRequired = false;
                hasSuccessfulSample = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsExpectedTelemetryFailure(exception))
            {
                consecutiveFailures++;
                recoveryNoticeRequired = true;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (hasSuccessfulSample && consecutiveFailures <= 2)
                    {
                        HomePanel.ShowStale();
                        MonitoringPanel.ShowStale();
                    }
                    else
                    {
                        HomePanel.ShowUnavailable(_localDevice);
                        MonitoringPanel.ShowUnavailable();
                    }
                    if (consecutiveFailures == 1)
                    {
                        AddActivity(
                            "Windows telemetry was unavailable; a bounded retry is scheduled.",
                            "Performance");
                    }
                });
            }

            TimeSpan retryDelay = consecutiveFailures == 0
                ? TimeSpan.FromMilliseconds(
                    Volatile.Read(ref _telemetryIntervalMilliseconds))
                : TimeSpan.FromSeconds(Math.Min(10, 2 + consecutiveFailures * 2));
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsExpectedTelemetryFailure(Exception exception) =>
        exception is InvalidOperationException or IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or NotSupportedException or
            DllNotFoundException or EntryPointNotFoundException;

    private async Task RefreshAllAsync()
    {
        await RunBusyAsync(async cancellationToken =>
        {
            ProtectionMonitorUpdate update = _protectionMonitor is null
                ? new ProtectionMonitorUpdate(
                    await _runtime.Defender.GetHealthAsync(cancellationToken),
                    LastKnownGood: null,
                    ProtectionMonitorState.Current,
                    ConsecutiveFailures: 0,
                    LastSuccessfulCheckAtUtc: null,
                    QueryDuration: TimeSpan.Zero,
                    NextRefreshIn: TimeSpan.FromMinutes(1),
                    Detail: "Manual protection health observation.")
                : await _protectionMonitor.RefreshOnceAsync(cancellationToken);
            DefenderHealthSnapshot health = update.Observed;
            RenderProtectionUpdate(update);
            await RefreshDefenderEventsAsync(cancellationToken);
            await RefreshQuarantineAsync(cancellationToken);
            await _runtime.AuditLog.AppendAsync(
                "health.refresh",
                health.IsProtected ? SecurityEventSeverity.Information : SecurityEventSeverity.Warning,
                health.Summary,
                detail: health.Error,
                cancellationToken: cancellationToken);
            AddSecuritySessionActivity(health.Summary + ".");
        });
    }

    private void RenderHealth(DefenderHealthSnapshot health)
    {
        Brush good = (Brush)FindResource("SignalBrush");
        Brush warning = (Brush)FindResource("WarningBrush");
        Brush danger = (Brush)FindResource("DangerBrush");
        Brush stateBrush = health.IsProtected
            ? good
            : health.WindowsSecurityCenterHealth == WindowsSecurityHealth.Poor ? danger : warning;

        Brush stateSurface = (Brush)FindResource(health.IsProtected
            ? "SignalSurfaceBrush"
            : health.WindowsSecurityCenterHealth == WindowsSecurityHealth.Poor
                ? "DangerSurfaceBrush"
                : "WarningSurfaceBrush");
        Brush stateBorder = (Brush)FindResource(health.IsProtected
            ? "SignalBorderBrush"
            : health.WindowsSecurityCenterHealth == WindowsSecurityHealth.Poor
                ? "DangerBorderBrush"
                : "WarningBorderBrush");

        HealthDot.Fill = stateBrush;
        HealthHero.Background = stateSurface;
        HealthHero.BorderBrush = stateBorder;
        HealthTitle.Text = health.Summary;

        ProtectionSummaryDot.Fill = stateBrush;
        ProtectionSummaryBorder.Background = stateSurface;
        ProtectionSummaryBorder.BorderBrush = stateBorder;
        ProtectionSummaryTitle.Text = health.IsProtected
            ? "Protection"
            : health.WindowsSecurityCenterHealth == WindowsSecurityHealth.Poor
                ? "Protection needs attention"
                : "Protection status";
        ProtectionSummaryState.Text = health.IsProtected
            ? "ACTIVE"
            : health.WindowsSecurityCenterHealth == WindowsSecurityHealth.Poor ? "ATTENTION" : "CHECK";
        ProtectionSummaryState.Foreground = stateBrush;
        ProtectionSummaryDetail.Text = health.IsProtected
            ? "Windows provider reports healthy"
            : "Open Security for provider details";
        string wsc = health.WindowsSecurityCenterHealth.ToString();
        string mode = string.IsNullOrWhiteSpace(health.AMRunningMode)
            ? "mode unavailable"
            : health.AMRunningMode;
        HealthDetail.Text = health.StatusQuerySucceeded
            ? $"WSC {wsc} · Defender {mode} · intelligence " +
              $"{health.AntivirusSignatureVersion ?? "version unavailable"} · checked {health.CheckedAtUtc.ToLocalTime():t}"
            : $"WSC {wsc} · {health.Error ?? "Defender details are managed by the registered provider."}";

        SetState(RealTimeStatus, health.RealTimeProtectionEnabled, health.StatusQuerySucceeded);
        SetState(BehaviorStatus, health.BehaviorMonitorEnabled, health.StatusQuerySucceeded);
        SetState(CloudStatus, health.CloudProtectionEnabled, health.StatusQuerySucceeded);
        if (!health.StatusQuerySucceeded)
        {
            SignatureStatus.Text = "Provider-managed";
            SignatureStatus.Foreground = warning;
        }
        else if (health.SignaturesOutOfDate)
        {
            SignatureStatus.Text = "Update needed";
            SignatureStatus.Foreground = warning;
        }
        else
        {
            SignatureStatus.Text = "Current";
            SignatureStatus.Foreground = good;
        }
    }

    private async Task RefreshAudioAsync()
    {
        try
        {
            Task<AudioEndpointSnapshot> endpointCapture = AudioEndpointProvider.CaptureAsync();
            Task<AudioSessionSnapshot> sessionCapture = AudioSessionProvider.CaptureAsync();
            await Task.WhenAll(endpointCapture, sessionCapture);
            _lastAudioSessionSnapshot = await sessionCapture;
            MixerPanel.UpdateSnapshot(
                await endpointCapture,
                _lastAudioSessionSnapshot);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ExternalException)
        {
            _lastAudioSessionSnapshot = null;
            MixerPanel.ShowUnavailable();
        }
    }

    private async void MixerPanel_RefreshRequested(object? sender, EventArgs e) =>
        await RefreshAudioAsync();

    private async void MixerPanel_SessionChangeRequested(
        object? sender,
        AudioSessionChangeRequestedEventArgs e)
    {
        AudioSessionMutationResult result;
        try
        {
            result = e.Kind == AudioSessionMutationKind.Volume
                ? await AudioSessionController.SetVolumeAsync(
                    e.Session,
                    e.RequestedVolumePercent ?? double.NaN)
                : await AudioSessionController.SetMuteAsync(
                    e.Session,
                    e.RequestedMute ?? e.Session.IsMuted);
        }
        catch (Exception exception) when (
            exception is COMException or InvalidOperationException or ExternalException)
        {
            result = new AudioSessionMutationResult(
                e.Kind,
                AudioSessionMutationStatus.Unavailable,
                e.Session.Name,
                e.RequestedVolumePercent,
                e.RequestedMute,
                null,
                null,
                $"Windows could not apply the requested audio change for {e.Session.Name}.");
        }

        await RefreshAudioAsync();
        MixerPanel.ShowSessionMutationResult(result);
        AddActivity(result.Message, "Audio");
    }

    private void MixerPanel_EndpointPreferenceRequested(
        object? sender,
        AudioEndpointPreferenceRequestedEventArgs e)
    {
        AudioEndpoint endpoint = e.Endpoint;
        string preferenceKey =
            SoltexPreferences.NormalizeEndpointPreferenceKey(endpoint.PreferenceKey);
        if (endpoint.State != AudioEndpointState.Active ||
            !Enum.IsDefined(endpoint.Direction) ||
            preferenceKey.Length == 0 ||
            !string.Equals(preferenceKey, endpoint.PreferenceKey, StringComparison.Ordinal))
        {
            MixerPanel.ShowEndpointPreferenceResult(
                saved: false,
                "Only a current, active audio endpoint can be remembered. No Windows audio setting was changed.");
            return;
        }

        SoltexPreferences requested = endpoint.Direction == AudioEndpointDirection.Render
            ? _preferences with
            {
                PreferredPlaybackEndpointKey = preferenceKey,
                LastWorkspace = _activeWorkspace
            }
            : _preferences with
            {
                PreferredRecordingEndpointKey = preferenceKey,
                LastWorkspace = _activeWorkspace
            };
        string direction = endpoint.Direction == AudioEndpointDirection.Render
            ? "playback"
            : "recording";
        SaveAudioEndpointPreferences(
            requested,
            $"{endpoint.Name} is remembered as the {direction} fallback reminder.",
            $"Remembered {endpoint.Name} as the {direction} fallback reminder.");
    }

    private void MixerPanel_ClearEndpointPreferencesRequested(object? sender, EventArgs e) =>
        SaveAudioEndpointPreferences(
            _preferences with
            {
                PreferredPlaybackEndpointKey = string.Empty,
                PreferredRecordingEndpointKey = string.Empty,
                LastWorkspace = _activeWorkspace
            },
            "Audio fallback reminders were cleared from this Windows account.",
            "Cleared the saved audio fallback reminders.");

    private void SaveAudioEndpointPreferences(
        SoltexPreferences requested,
        string successDetail,
        string activityDetail)
    {
        SoltexPreferences normalized = requested.Normalize();
        try
        {
            _preferencesStore.Save(normalized);
            _preferences = normalized;
            MixerPanel.UpdateEndpointPreferences(
                _preferences.PreferredPlaybackEndpointKey,
                _preferences.PreferredRecordingEndpointKey);
            MixerPanel.ShowEndpointPreferenceResult(saved: true, detail: successDetail);
            SettingsPanel.UpdatePreferences(
                _preferences,
                recoveredFromInvalid: false,
                "Audio fallback preferences are saved on this Windows account.");
            AddActivity(activityDetail, "Audio");
        }
        catch (Exception exception) when (IsExpectedPreferenceWriteFailure(exception))
        {
            MixerPanel.UpdateEndpointPreferences(
                _preferences.PreferredPlaybackEndpointKey,
                _preferences.PreferredRecordingEndpointKey);
            MixerPanel.ShowEndpointPreferenceResult(
                saved: false,
                "The fallback reminder could not be saved. No Windows audio setting was changed.");
        }
    }

    private void MixerPanel_OpenSoundSettingsRequested(object? sender, EventArgs e)
    {
        WindowsSoundSettingsLaunchResult result = WindowsSoundSettingsLauncher.Open();
        MixerPanel.ShowSoundSettingsResult(result);
        AddActivity(result.Message, "Audio");
    }

    private async void ApplicationsPanel_RefreshRequested(object? sender, EventArgs e) =>
        await RefreshApplicationsAsync();

    private async Task RefreshApplicationsAsync()
    {
        if (!await _applicationRefreshGate.WaitAsync(0))
        {
            return;
        }

        ApplicationsPanel.ShowLoading();
        try
        {
            Task<ApplicationInventorySnapshot> applicationCapture =
                ApplicationInventoryProvider.CaptureAsync();
            Task<WindowsServiceInventorySnapshot> serviceCapture =
                WindowsServiceInventoryProvider.CaptureAsync();
            await Task.WhenAll(applicationCapture, serviceCapture);
            ApplicationsPanel.UpdateSnapshot(
                await applicationCapture,
                await serviceCapture);
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           System.Security.SecurityException or
                                           PlatformNotSupportedException or
                                           InvalidOperationException)
        {
            ApplicationsPanel.ShowUnavailable();
        }
        finally
        {
            _applicationRefreshGate.Release();
        }
    }

    private void RenderProtectionUpdate(ProtectionMonitorUpdate update)
    {
        RenderHealth(update.Observed);
        if (update.State == ProtectionMonitorState.Degraded && update.LastKnownGood is not null)
        {
            HealthDetail.Text += $" · last confirmed {update.LastKnownGood.CheckedAtUtc.ToLocalTime():t}";
        }
    }

    private async Task RefreshDefenderEventsAsync(CancellationToken cancellationToken)
    {
        DefenderEventQueryResult result = await _runtime.Defender.GetRecentEventsAsync(
            TimeSpan.FromDays(7),
            maximumEvents: 24,
            cancellationToken);
        if (!result.Succeeded)
        {
            _defenderEventRows.Clear();
            _securityActivityVisible = false;
            DefenderEventGrid.Visibility = Visibility.Collapsed;
            EventQueryStatus.Text = "Unavailable";
            EventQueryStatus.Foreground = (Brush)FindResource("WarningBrush");
            SecurityActivityButton.Content = "Activity unavailable";
            SecurityActivityButton.IsEnabled = false;
            AddActivity(
                "Defender activity unavailable: " + result.Error,
                "Security");
            return;
        }

        _defenderEventRows.Clear();
        foreach (DefenderOperationalEvent item in result.Events)
        {
            _defenderEventRows.Add(new DefenderEventRow(item));
        }

        EventQueryStatus.Text = $"{result.Events.Count} events · {result.Duration.TotalMilliseconds:F0} ms";
        EventQueryStatus.Foreground = (Brush)FindResource("MutedBrush");
        if (result.Events.Count == 0)
        {
            _securityActivityVisible = false;
            DefenderEventGrid.Visibility = Visibility.Collapsed;
        }
        SecurityActivityButton.IsEnabled = result.Events.Count > 0;
        UpdateSecurityActivityButton();
    }

    private void SecurityActivity_Click(object sender, RoutedEventArgs e)
    {
        _securityActivityVisible = !_securityActivityVisible;
        DefenderEventGrid.Visibility = _securityActivityVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateSecurityActivityButton();
    }

    private void UpdateSecurityActivityButton()
    {
        SecurityActivityButton.Content = _securityActivityVisible
            ? "Hide activity"
            : $"Show activity ({_defenderEventRows.Count})";
        SecurityActivityButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            _securityActivityVisible
                ? "Hide recent Windows protection activity"
                : $"Show {_defenderEventRows.Count} recent Windows protection events");
    }

    private void OnProtectionMonitorUpdated(ProtectionMonitorUpdate update)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            RenderProtectionUpdate(update);
            if (_lastMonitorState == update.State)
            {
                return;
            }

            _lastMonitorState = update.State;
            if (update.State is not (ProtectionMonitorState.Degraded or ProtectionMonitorState.Recovered))
            {
                return;
            }

            SecurityEventSeverity severity = update.State == ProtectionMonitorState.Degraded
                ? SecurityEventSeverity.Warning
                : SecurityEventSeverity.Information;
            AddActivity(update.Detail, "Security");
            await _runtime.AuditLog.AppendAsync(
                update.State == ProtectionMonitorState.Degraded
                    ? "monitor.degraded"
                    : "monitor.recovered",
                severity,
                update.Detail,
                detail: update.Observed.Error);
        });
    }

    private void SetState(System.Windows.Controls.TextBlock control, bool enabled, bool known)
    {
        control.Text = !known ? "Provider-managed" : enabled ? "Active" : "Attention";
        control.Foreground = (Brush)FindResource(!known
            ? "WarningBrush"
            : enabled ? "SignalBrush" : "DangerBrush");
    }

    private async Task RefreshQuarantineAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuarantineEntry> entries = await _runtime.Quarantine.ListAsync(cancellationToken);
        _quarantineRows.Clear();
        foreach (QuarantineEntry entry in entries)
        {
            _quarantineRows.Add(new QuarantineRow(entry));
        }

        QuarantineCount.Text = entries.Count == 1 ? "1 item" : $"{entries.Count} items";
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (_operationCancellation is not null || _shutdownStarted)
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> drained = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _operationCancellation = cancellation;
        _activeOperationDrained = drained.Task;
        SetBusy(true);
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                AddActivity(
                    "Operation cancelled. A Defender scan already accepted by Windows may continue in the background.",
                    "Security");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (!_shutdownStarted)
            {
                AddActivity("Operation failed: " + exception.Message, "Security");
                MessageBox.Show(this, exception.Message, "Soltex Security", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            cancellation.Dispose();
            if (!_shutdownStarted)
            {
                SetBusy(false);
            }

            drained.TrySetResult(true);
        }
    }

    private void SetBusy(bool busy)
    {
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        QuickScanButton.IsEnabled = !busy;
        FileScanButton.IsEnabled = !busy;
        FolderScanButton.IsEnabled = !busy;
        UpdateButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
    }

    private async Task RunDefenderCommandAsync(
        Func<CancellationToken, Task<DefenderCommandResult>> command)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            DefenderCommandResult result = await command(cancellationToken);
            SecurityEventSeverity severity = result.Succeeded
                ? SecurityEventSeverity.Information
                : SecurityEventSeverity.Warning;
            await _runtime.AuditLog.AppendAsync(
                "defender.command",
                severity,
                result.Operation,
                detail: result.Message,
                cancellationToken: cancellationToken);
            AddActivity($"{result.Operation}: {result.Message}", "Security");
            if (!result.Succeeded)
            {
                MessageBox.Show(this, result.Message, result.Operation, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            DefenderHealthSnapshot health = await _runtime.Defender.GetHealthAsync(cancellationToken);
            RenderHealth(health);
        });
    }

    private async Task ScanFileAsync(string path)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            FileAssessment assessment = await _runtime.Assessor.AssessAsync(path, cancellationToken);
            SecurityEventSeverity severity = assessment.ShouldBlock
                ? SecurityEventSeverity.Critical
                : assessment.Verdict == ContentVerdict.ReviewRecommended
                    ? SecurityEventSeverity.Warning
                    : SecurityEventSeverity.Information;
            await _runtime.AuditLog.AppendAsync(
                "file.assessment",
                severity,
                assessment.Verdict.ToString(),
                assessment.Path,
                assessment.Detail,
                cancellationToken);
            AddActivity(
                $"Selected file assessment: {assessment.Verdict}.",
                "Security");

            if (assessment.ShouldBlock && File.Exists(path))
            {
                MessageBoxResult choice = MessageBox.Show(
                    this,
                    assessment.Detail + "\n\nMove this file into Soltex quarantine?",
                    "Threat detected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (choice == MessageBoxResult.Yes)
                {
                    QuarantineEntry entry = await _runtime.Quarantine.QuarantineAsync(
                        path,
                        assessment.Detail,
                        cancellationToken);
                    await _runtime.AuditLog.AppendAsync(
                        "quarantine.add",
                        SecurityEventSeverity.Critical,
                        "A file was moved into Soltex quarantine.",
                        entry.OriginalPath,
                        entry.Detection,
                        cancellationToken);
                    AddActivity("Selected file moved to quarantine.", "Security");
                    await RefreshQuarantineAsync(cancellationToken);
                    return;
                }
            }

            if (File.Exists(path))
            {
                DefenderCommandResult defenderResult = await _runtime.Defender.RunCustomScanAsync(path, cancellationToken);
                AddActivity(
                    $"Defender custom scan: {defenderResult.Message}",
                    "Security");
            }
        });
    }

    private async Task HandleImportAssessmentAsync(FileAssessment assessment)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            SecurityEventSeverity severity = assessment.ShouldBlock
                ? SecurityEventSeverity.Critical
                : assessment.Verdict == ContentVerdict.ReviewRecommended
                    ? SecurityEventSeverity.Warning
                    : SecurityEventSeverity.Information;
            await _runtime.AuditLog.AppendAsync(
                "import.assessment",
                severity,
                assessment.Verdict.ToString(),
                assessment.Path,
                assessment.Detail);
            AddActivity(
                $"Import guard assessed a local item · {assessment.Verdict}.",
                "Security");
        });
    }

    private void AddActivity(string message, string area = "System")
    {
        ActivityMutationResult result =
            _activityStore.Add(area, message, _preferences.ActivityRetention);
        ActivityPanel.UpdateEntries(
            _activityStore.Snapshot(),
            _preferences.ActivityRetention,
            result.Detail,
            result.StorageHealthy);

        if (!string.Equals(area, "Security", StringComparison.Ordinal))
        {
            return;
        }

        AddSecuritySessionActivity(
            result.Entry?.Summary ?? "Security activity recorded.");
    }

    private void AddSecuritySessionActivity(string message)
    {
        ActivityEntry entry =
            ActivityEntry.Create("Security", message, DateTimeOffset.UtcNow);
        ActivityList.Items.Insert(0, $"{DateTimeOffset.Now:t}  {entry.Summary}");
        while (ActivityList.Items.Count > 40)
        {
            ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void QuickScan_Click(object sender, RoutedEventArgs e) =>
        await RunDefenderCommandAsync(_runtime.Defender.RunQuickScanAsync);

    private async void Update_Click(object sender, RoutedEventArgs e) =>
        await RunDefenderCommandAsync(_runtime.Defender.UpdateSecurityIntelligenceAsync);

    private async void FileScan_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose a file to assess and scan",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ScanFileAsync(dialog.FileName);
        }
    }

    private async void FolderScan_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose a folder for Windows Defender to scan",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await RunDefenderCommandAsync(cancellationToken =>
                _runtime.Defender.RunCustomScanAsync(dialog.FolderName, cancellationToken));
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineGrid.SelectedItem is not QuarantineRow row)
        {
            MessageBox.Show(this, "Select a quarantine item first.", "Soltex Security");
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "Only restore this file if you have independently confirmed it is safe. Continue?",
            "Restore quarantine item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            string restoredPath = await _runtime.Quarantine.RestoreAsync(row.Entry.Id, cancellationToken);
            await _runtime.AuditLog.AppendAsync(
                "quarantine.restore",
                SecurityEventSeverity.Warning,
                "A quarantine item was restored by the user.",
                restoredPath,
                cancellationToken: cancellationToken);
            AddActivity("A quarantined item was restored.", "Security");
            await RefreshQuarantineAsync(cancellationToken);
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineGrid.SelectedItem is not QuarantineRow row)
        {
            MessageBox.Show(this, "Select a quarantine item first.", "Soltex Security");
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "Permanently delete this quarantined file? This cannot be undone.",
            "Delete quarantine item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            await _runtime.Quarantine.DeleteAsync(row.Entry.Id, cancellationToken);
            await _runtime.AuditLog.AppendAsync(
                "quarantine.delete",
                SecurityEventSeverity.Warning,
                "A quarantine item was permanently deleted by the user.",
                cancellationToken: cancellationToken);
            AddActivity(
                "A quarantined item was permanently deleted.",
                "Security");
            await RefreshQuarantineAsync(cancellationToken);
        });
    }

    private void OpenWindowsSecurity_Click(object sender, RoutedEventArgs e) => OpenUri("windowsdefender:");

    private void OpenImports_Click(object sender, RoutedEventArgs e) => OpenUri(_runtime.ImportsPath);

    private void SelectRustDesk_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select the external RustDesk client",
            CheckFileExists = true,
            Multiselect = false,
            FileName = "RustDesk.exe",
            Filter = "RustDesk executable (RustDesk.exe)|RustDesk.exe"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!RemoteAssistExecutable.TryCreate(
                dialog.FileName,
                out RemoteAssistExecutable? executable,
                out string error))
        {
            MessageBox.Show(this, error, "RustDesk selection rejected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetRemoteAssistExecutable(executable);
    }

    private async void ShareRemote_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTrustedRemoteClient(out RemoteAssistExecutable? executable))
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Soltex will open the external RustDesk interface. Soltex does not expose your screen by itself, create credentials, enable unattended access, or bypass the remote client's consent controls. Continue?",
            "Open screen sharing",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        RemoteAssistLaunchResult result = RustDeskExternalClient.Launch(
            RustDeskExternalClient.CreateSharePlan(executable!));
        await RecordRemoteLaunchAsync(RemoteAssistMode.ShareThisDevice, result);
    }

    private async void ConnectRemote_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTrustedRemoteClient(out RemoteAssistExecutable? executable))
        {
            return;
        }

        if (!RemotePeerId.TryCreate(RemotePeerIdInput.Text, out RemotePeerId? peerId, out string error))
        {
            MessageBox.Show(this, error, "Peer ID rejected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Soltex will pass this peer ID to the external RustDesk client using its documented connect command. No password, elevation flag, service command, or consent bypass is supplied. Confirm that you are authorized to control the remote device, then continue.",
            "Connect to authorized peer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        RemoteAssistLaunchResult result = RustDeskExternalClient.Launch(
            RustDeskExternalClient.CreateControlPlan(executable!, peerId!));
        await RecordRemoteLaunchAsync(RemoteAssistMode.ControlPeer, result);
    }

    private void RemotePeerId_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool valid = RemotePeerId.TryCreate(RemotePeerIdInput.Text, out _, out string error);
        RemotePeerHint.Text = valid
            ? "ID format accepted · authorization is still required"
            : error;
        RemotePeerHint.Foreground = (Brush)FindResource(valid ? "SignalBrush" : "MutedBrush");
        RemoteConnectButton.IsEnabled = valid && _remoteAssistTrust?.IsTrusted == true;
    }

    private void OpenRustDeskSource_Click(object sender, RoutedEventArgs e) =>
        OpenUri("https://github.com/rustdesk/rustdesk");

    private void SetRemoteAssistExecutable(RemoteAssistExecutable? executable)
    {
        _remoteAssistExecutable = executable;
        if (executable is null)
        {
            _remoteAssistTrust = null;
        }
        else
        {
            try
            {
                _remoteAssistTrust = AuthenticodeVerifier.Verify(executable.FullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _remoteAssistTrust = new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Error,
                    NativeStatus: -1,
                    Detail: "Soltex could not verify the selected external client: " + exception.Message);
            }
        }

        bool trusted = _remoteAssistTrust?.IsTrusted == true;
        RemoteExecutablePath.Text = executable?.FullPath ?? "No external client selected";
        RemoteFingerprint.Text = executable is null
            ? "SHA-256 unavailable"
            : "SHA-256 " + executable.Sha256;
        RemoteTrustState.Text = executable is null
            ? "RustDesk is not connected"
            : trusted ? "Trusted external client" : "Signature trust failed";
        RemoteTrustDetail.Text = executable is null
            ? "Choose an installed, signed RustDesk.exe. Soltex never bundles or silently downloads the remote-control runtime."
            : _remoteAssistTrust!.Detail;
        RemoteTrustDot.Fill = (Brush)FindResource(executable is null
            ? "MutedBrush"
            : trusted ? "SignalBrush" : "DangerBrush");
        RemoteTrustPill.Text = executable is null ? "NOT CONNECTED" : trusted ? "READY" : "BLOCKED";
        RemoteTrustPill.Foreground = (Brush)FindResource(trusted ? "SignalBrush" : "WarningBrush");
        RemoteShareButton.IsEnabled = trusted;

        bool validPeer = RemotePeerId.TryCreate(RemotePeerIdInput.Text, out _, out _);
        RemoteConnectButton.IsEnabled = trusted && validPeer;
    }

    private bool TryGetTrustedRemoteClient(out RemoteAssistExecutable? executable)
    {
        executable = _remoteAssistExecutable;
        if (executable is not null && !executable.VerifyUnchanged(out string unchangedError))
        {
            _remoteAssistTrust = new AuthenticodeVerificationResult(
                AuthenticodeStatus.Error,
                NativeStatus: -1,
                Detail: unchangedError);
            RemoteTrustState.Text = "Client changed after approval";
            RemoteTrustDetail.Text = unchangedError;
            RemoteTrustDot.Fill = (Brush)FindResource("DangerBrush");
            RemoteTrustPill.Text = "BLOCKED";
            RemoteTrustPill.Foreground = (Brush)FindResource("DangerBrush");
            RemoteShareButton.IsEnabled = false;
            RemoteConnectButton.IsEnabled = false;
        }
        else if (executable is not null)
        {
            SetRemoteAssistExecutable(executable);
            if (_remoteAssistTrust?.IsTrusted == true)
            {
                return true;
            }
        }

        MessageBox.Show(
            this,
            "Select a RustDesk.exe whose Authenticode signature is trusted before starting a remote-assistance flow.",
            "Trusted external client required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private async Task RecordRemoteLaunchAsync(RemoteAssistMode mode, RemoteAssistLaunchResult result)
    {
        string operation = mode == RemoteAssistMode.ShareThisDevice ? "share interface" : "authorized peer connection";
        RemoteSessionStatus.Text = result.Started ? "External client started" : "Launch failed";
        RemoteSessionDetail.Text = result.Message;
        RemoteSessionStatus.Foreground = (Brush)FindResource(result.Started ? "SignalBrush" : "DangerBrush");
        AddActivity(
            $"Remote Assist {operation}: {result.Message}",
            "Remote Assist");
        try
        {
            await _runtime.AuditLog.AppendAsync(
                "remote_assist.launch",
                result.Started ? SecurityEventSeverity.Information : SecurityEventSeverity.Warning,
                $"External RustDesk {operation} launch {(result.Started ? "started" : "failed")}.",
                detail: result.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AddActivity(
                "Remote Assist audit write failed: " + exception.Message,
                "Remote Assist");
        }
    }

    private async Task RefreshUpdateJournalAsync()
    {
        UpdateInspectButton.IsEnabled = false;
        try
        {
            UpdatePlanningRecoveryReport report = await _updateJournal.InspectAsync(_updateStagingRoot);
            _updateJournalRows.Clear();
            foreach (UpdatePlanningJournalEntry entry in report.Entries
                         .OrderByDescending(item => item.TimestampUtc)
                         .Take(32))
            {
                _updateJournalRows.Add(new UpdateJournalRow(entry));
            }

            int entryCount = report.Entries.Count;
            UpdateJournalCount.Text = entryCount == 1 ? "1 entry" : $"{entryCount} entries";
            bool reviewRequired = report.HasIncompletePlanningAttempt ||
                                  report.ExistingPrivateStagingTokens.Count > 0;
            Brush stateBrush = (Brush)FindResource("WarningBrush");
            UpdateStateDot.Fill = stateBrush;
            UpdateReadinessHero.BorderBrush = (Brush)FindResource("WarningBorderBrush");
            UpdateReadinessHero.Background = (Brush)FindResource("WarningSurfaceBrush");
            UpdateStateTitle.Text = reviewRequired ? "Cleanup review required" : "Updates are not configured";
            UpdateStatePill.Text = reviewRequired ? "REVIEW" : "OFF";
            UpdateStatePill.Foreground = stateBrush;
            UpdateStateDetail.Text = reviewRequired
                ? report.Detail
                : "No signed release source is configured, and no incomplete planning attempt is recorded.";
            UpdateRecoveryTitle.Text = reviewRequired
                ? "Interrupted evidence exists"
                : "No interrupted attempt";
            UpdateRecoveryTitle.Foreground = stateBrush;
            UpdateRecoveryDetail.Text = reviewRequired
                ? $"{report.ExistingPrivateStagingTokens.Count} private staging token(s) require explicit owner review. Nothing was activated."
                : "No private staging token requires review.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or CryptographicException)
        {
            Brush danger = (Brush)FindResource("DangerBrush");
            _updateJournalRows.Clear();
            UpdateJournalCount.Text = "Unavailable";
            UpdateStateDot.Fill = danger;
            UpdateReadinessHero.BorderBrush = danger;
            UpdateStateTitle.Text = "Planning evidence unavailable";
            UpdateStatePill.Text = "FAILED CLOSED";
            UpdateStatePill.Foreground = danger;
            UpdateStateDetail.Text = "Soltex could not authenticate the local planning journal. No update action is available.";
            UpdateRecoveryTitle.Text = "Manual review required";
            UpdateRecoveryTitle.Foreground = danger;
            UpdateRecoveryDetail.Text =
                "The authenticated journal could not be read. No path or update action is exposed from this failed-closed state.";
        }
        finally
        {
            UpdateInspectButton.IsEnabled = true;
        }
    }

    private async void UpdateInspect_Click(object sender, RoutedEventArgs e) =>
        await RefreshUpdateJournalAsync();

    private void OpenUri(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to open", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPanel(HomePanel, HomeNavButton);

    private void MonitoringNav_Click(object sender, RoutedEventArgs e) => ShowPanel(MonitoringPanel, MonitoringNavButton);

    private async void ApplicationsNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(ApplicationsPanel, ApplicationsNavButton);
        await RefreshApplicationsAsync();
    }

    private void DevicesNav_Click(object sender, RoutedEventArgs e) => ShowPanel(DevicesPanel, DevicesNavButton);

    private async void MixerNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(MixerPanel, MixerNavButton);
        await RefreshAudioAsync();
    }

    private void ClipsNav_Click(object sender, RoutedEventArgs e) => ShowPanel(ClipsPanel, ClipsNavButton);

    private void SecurityNav_Click(object sender, RoutedEventArgs e) => ShowPanel(SecurityPanel, SecurityNavButton);

    private void RemoteNav_Click(object sender, RoutedEventArgs e) => ShowPanel(RemotePanel, RemoteNavButton);

    private void WhisperNav_Click(object sender, RoutedEventArgs e) => ShowPanel(WhisperPanel, WhisperNavButton);

    /// <summary>
    /// Shows the listening surface in its idle-listening state so the user can see and
    /// place it before any capture exists. It renders a presenter frame like the real
    /// session would; it does not open a microphone.
    /// </summary>
    private void ShowWhisperOverlayPreview()
    {
        if (_whisperOverlay is null)
        {
            _whisperOverlay = new WhisperOverlayWindow { Owner = this };
            _whisperOverlay.ActionRequested += (_, _) => _whisperOverlayAction?.Invoke();
            _whisperOverlay.Closed += (_, _) =>
            {
                _whisperOverlay = null;
                _whisperOverlayAction = null;
            };
        }

        _whisperOverlayAction = () =>
        {
            if (!_whisperCaptureDrained.IsCompleted)
            {
                _whisperCapture?.CompleteCurrentCapture();
            }
            else
            {
                _whisperOverlay?.Hide();
            }
        };

        WhisperOverlayView frame = WhisperOverlayPresenter.Project(new WhisperOverlayInputs(
            new WhisperSessionSnapshot(
                WhisperSessionState.Listening,
                WhisperCaptureMode.PushToTalk,
                DateTimeOffset.UtcNow,
                null),
            WhisperCaptureMode.PushToTalk,
            TargetProcessName: null,
            TargetIsKnown: false,
            HandsFreeLocked: false,
            TimeSpan.Zero,
            WhisperDurationState.Current,
            WhisperDeliveryKind.None,
            ErrorDetail: null));

        _whisperOverlay.Render(frame);
        _whisperOverlay.Left = Left + ((Width - _whisperOverlay.Width) / 2);
        _whisperOverlay.Top = Top + Height - 140;
    }

    private void ActivityNav_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(ActivityPanel, ActivityNavButton);

    private void UpdateNav_Click(object sender, RoutedEventArgs e) => ShowPanel(UpdatePanel, UpdateNavButton);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPanel(SettingsPanel, SettingsNavButton);

    private void SettingsPanel_PreferencesChanged(
        object? sender,
        PreferencesChangedEventArgs e)
    {
        SoltexPreferences requested = e.Preferences.Normalize() with
        {
            LastWorkspace = _activeWorkspace
        };
        bool shorteningActivityRetention =
            ActivityRetentionRank(requested.ActivityRetention) <
            ActivityRetentionRank(_preferences.ActivityRetention);
        if (shorteningActivityRetention)
        {
            string retentionImpact =
                requested.ActivityRetention == ActivityRetention.SessionOnly
                    ? "Saved Activity history on this Windows account will be removed. Current-session entries remain visible."
                    : "Saved Activity entries older than 7 days will be removed.";
            MessageBoxResult choice = MessageBox.Show(
                this,
                "Shorten Activity retention?\n\n" + retentionImpact,
                "Change Activity retention",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
            {
                SettingsPanel.UpdatePreferences(
                    _preferences,
                    recoveredFromInvalid: false,
                    "Activity retention was not changed.");
                return;
            }
        }

        bool closeBehaviorChanged =
            requested.CloseBehavior != _preferences.CloseBehavior;
        _preferences = requested;
        Volatile.Write(
            ref _telemetryIntervalMilliseconds,
            _preferences.TelemetryIntervalMilliseconds);
        MonitoringPanel.SetDetailsVisible(_preferences.OpenPerformanceDetails);
        ActivityMutationResult retentionResult = _activityStore.SetRetention(
            _preferences.ActivityRetention,
            removePersistedWhenSessionOnly:
                shorteningActivityRetention &&
                _preferences.ActivityRetention == ActivityRetention.SessionOnly);
        ActivityPanel.UpdateEntries(
            _activityStore.Snapshot(),
            _preferences.ActivityRetention,
            retentionResult.Detail,
            retentionResult.StorageHealthy);
        try
        {
            _preferencesStore.Save(_preferences);
            SettingsPanel.ShowSaved();
        }
        catch (Exception exception) when (IsExpectedPreferenceWriteFailure(exception))
        {
            SettingsPanel.ShowSaveFailure();
        }

        if (closeBehaviorChanged)
        {
            CloseBehaviorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static int ActivityRetentionRank(ActivityRetention retention) =>
        retention switch
        {
            ActivityRetention.ThirtyDays => 2,
            ActivityRetention.SevenDays => 1,
            _ => 0
        };

    private void ActivityPanel_ClearRequested(object? sender, EventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Clear all visible and saved Soltex Activity history?",
            "Clear Activity",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        ActivityMutationResult result = _activityStore.Clear();
        ActivityPanel.UpdateEntries(
            _activityStore.Snapshot(),
            _preferences.ActivityRetention,
            result.Detail,
            result.StorageHealthy);
    }

    private void SavePreferencesForClose()
    {
        _preferences = _preferences with
        {
            LastWorkspace = _activeWorkspace
        };
        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (IsExpectedPreferenceWriteFailure(exception))
        {
            // Closing must remain available when a local preference cannot be persisted.
        }
    }

    private static bool IsExpectedPreferenceWriteFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or InvalidOperationException;

    private void RestoreWorkspace(string workspace)
    {
        switch (SoltexPreferences.NormalizeWorkspace(workspace))
        {
            case "monitoring":
                ShowPanel(MonitoringPanel, MonitoringNavButton);
                break;
            case "applications":
                ShowPanel(ApplicationsPanel, ApplicationsNavButton);
                break;
            case "mixer":
                ShowPanel(MixerPanel, MixerNavButton);
                break;
            case "security":
                ShowPanel(SecurityPanel, SecurityNavButton);
                break;
            case "remote":
                ShowPanel(RemotePanel, RemoteNavButton);
                break;
            case "whisper":
                ShowPanel(WhisperPanel, WhisperNavButton);
                break;
            case "activity":
                ShowPanel(ActivityPanel, ActivityNavButton);
                break;
            case "updates":
                ShowPanel(UpdatePanel, UpdateNavButton);
                break;
            case "settings":
                ShowPanel(SettingsPanel, SettingsNavButton);
                break;
            default:
                ShowPanel(HomePanel, HomeNavButton);
                break;
        }
    }

    internal bool TrySelectRenderSmokePanel(string panelName)
    {
        string normalized = panelName.Trim();
        if (string.Equals(normalized, "home", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(HomePanel, HomeNavButton);
            return true;
        }

        if (string.Equals(normalized, "monitoring-details", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MonitoringPanel, MonitoringNavButton);
            MonitoringPanel.MonitoringDetailsButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            _renderSmokeFocusTarget = MonitoringPanel.MonitoringDetailsPanel;
            return true;
        }

        if (string.Equals(normalized, "monitoring", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "monitor", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MonitoringPanel, MonitoringNavButton);
            return true;
        }

        if (string.Equals(normalized, "monitoring-benchmark", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MonitoringPanel, MonitoringNavButton);
            MonitoringPanel.PrepareBenchmarkRenderState();
            _renderSmokeFocusTarget = MonitoringPanel.BenchmarkPanel;
            return true;
        }

        if (string.Equals(normalized, "applications-services", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(ApplicationsPanel, ApplicationsNavButton);
            ApplicationsPanel.ShowServicesForEvidence();
            _renderSmokeFocusTarget = ApplicationsPanel.ServicesGrid;
            return true;
        }

        if (string.Equals(normalized, "command-palette", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(HomePanel, HomeNavButton);
            OpenCommandPalette();
            _renderSmokeFocusTarget = CommandSearchBox;
            return true;
        }

        if (string.Equals(normalized, "applications", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "apps", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(ApplicationsPanel, ApplicationsNavButton);
            return true;
        }

        if (string.Equals(normalized, "devices", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "device", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(DevicesPanel, DevicesNavButton);
            return true;
        }

        if (string.Equals(normalized, "security-activity", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(SecurityPanel, SecurityNavButton);
            SecurityActivity_Click(SecurityActivityButton, new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            _renderSmokeFocusTarget = DefenderEventGrid;
            return true;
        }

        if (string.Equals(normalized, "security", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(SecurityPanel, SecurityNavButton);
            return true;
        }

        if (string.Equals(normalized, "remote", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(RemotePanel, RemoteNavButton);
            return true;
        }

        if (string.Equals(normalized, "whisper", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(WhisperPanel, WhisperNavButton);
            _renderSmokeFocusTarget = WhisperPanel.WhisperInputDevicePicker;
            return true;
        }

        if (string.Equals(
                normalized,
                "whisper-personalize",
                StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(WhisperPanel, WhisperNavButton);
            WhisperPanel.ShowPersonalizationForEvidence();
            _renderSmokeFocusTarget = WhisperPanel;
            return true;
        }

        if (string.Equals(normalized, "update", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "updates", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(UpdatePanel, UpdateNavButton);
            return true;
        }

        if (string.Equals(normalized, "mixer-devices", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MixerPanel, MixerNavButton);
            MixerPanel.DeviceDetailsButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            _renderSmokeFocusTarget = MixerPanel.EndpointPreferenceDetailText;
            return true;
        }

        if (string.Equals(normalized, "mixer-more", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MixerPanel, MixerNavButton);
            MixerPanel.DeviceDetailsButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            MixerPanel.MoreEndpointsButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            _renderSmokeFocusTarget = MixerPanel.MoreEndpointsPanel;
            return true;
        }

        if (string.Equals(normalized, "mixer", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(MixerPanel, MixerNavButton);
            return true;
        }

        if (string.Equals(normalized, "clips", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(ClipsPanel, ClipsNavButton);
            return true;
        }

        if (string.Equals(normalized, "activity", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(ActivityPanel, ActivityNavButton);
            return true;
        }

        if (string.Equals(normalized, "settings", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(SettingsPanel, SettingsNavButton);
            return true;
        }

        return false;
    }

    internal void PrepareRenderSmokeCapture()
    {
        _renderSmokeFocusTarget?.BringIntoView();
    }

    private void UpdatePreviewTab_Click(object sender, RoutedEventArgs e) =>
        ShowUpdatePanel(UpdatePreviewPanel, UpdatePreviewTab);

    private void UpdateJournalTab_Click(object sender, RoutedEventArgs e) =>
        ShowUpdatePanel(UpdateJournalPanel, UpdateJournalTab);

    private void UpdateRecoveryTab_Click(object sender, RoutedEventArgs e) =>
        ShowUpdatePanel(UpdateRecoveryPanel, UpdateRecoveryTab);

    private void ShowUpdatePanel(UIElement panel, System.Windows.Controls.Button selectedTab)
    {
        UpdatePreviewPanel.Visibility = panel == UpdatePreviewPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateJournalPanel.Visibility = panel == UpdateJournalPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateRecoveryPanel.Visibility = panel == UpdateRecoveryPanel
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (System.Windows.Controls.Button tab in new[]
                 {
                     UpdatePreviewTab,
                     UpdateJournalTab,
                     UpdateRecoveryTab
                 })
        {
            tab.Tag = tab == selectedTab ? "Selected" : null;
        }
    }

    private void ShowPanel(UIElement panel, System.Windows.Controls.Button selectedButton)
    {
        HomePanel.Visibility = panel == HomePanel ? Visibility.Visible : Visibility.Collapsed;
        MonitoringPanel.Visibility = panel == MonitoringPanel ? Visibility.Visible : Visibility.Collapsed;
        ApplicationsPanel.Visibility = panel == ApplicationsPanel ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = panel == ActivityPanel ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = panel == DevicesPanel ? Visibility.Visible : Visibility.Collapsed;
        MixerPanel.Visibility = panel == MixerPanel ? Visibility.Visible : Visibility.Collapsed;
        ClipsPanel.Visibility = panel == ClipsPanel ? Visibility.Visible : Visibility.Collapsed;
        SecurityPanel.Visibility = panel == SecurityPanel ? Visibility.Visible : Visibility.Collapsed;
        RemotePanel.Visibility = panel == RemotePanel ? Visibility.Visible : Visibility.Collapsed;
        WhisperPanel.Visibility = panel == WhisperPanel ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = panel == UpdatePanel ? Visibility.Visible : Visibility.Collapsed;
        CancelBenchmarkIfInactive();
        foreach (System.Windows.Controls.Button button in new[]
                 {
                     HomeNavButton,
                     MonitoringNavButton,
                     ApplicationsNavButton,
                     DevicesNavButton,
                     MixerNavButton,
                     ClipsNavButton,
                     SecurityNavButton,
                     RemoteNavButton,
                     WhisperNavButton,
                     ActivityNavButton,
                     UpdateNavButton,
                     SettingsNavButton
                 })
        {
            button.Tag = button == selectedButton ? "Selected" : null;
            button.Background = (Brush)FindResource("NavRestBrush");
            button.Foreground = (Brush)FindResource("MutedBrush");
        }

        _activeWorkspace =
            panel == MonitoringPanel ? "monitoring" :
            panel == ApplicationsPanel ? "applications" :
            panel == MixerPanel ? "mixer" :
            panel == SecurityPanel ? "security" :
            panel == RemotePanel ? "remote" :
            panel == WhisperPanel ? "whisper" :
            panel == ActivityPanel ? "activity" :
            panel == UpdatePanel ? "updates" :
            panel == SettingsPanel ? "settings" :
            "home";

        panel.BeginAnimation(OpacityProperty, null);
        panel.Opacity = 1;
        if (SystemParameters.ClientAreaAnimation)
        {
            panel.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
        }
    }

    private void StopWorkspaceAnimations()
    {
        foreach (UIElement panel in new UIElement[]
                 {
                     HomePanel,
                     MonitoringPanel,
                     ApplicationsPanel,
                     DevicesPanel,
                     MixerPanel,
                     ClipsPanel,
                     SecurityPanel,
                     RemotePanel,
                     WhisperPanel,
                     ActivityPanel,
                     UpdatePanel,
                     SettingsPanel
                 })
        {
            panel.BeginAnimation(OpacityProperty, null);
            panel.Opacity = 1;
        }
    }

    private sealed class QuarantineRow(QuarantineEntry entry)
    {
        public QuarantineEntry Entry { get; } = entry;
        public string FileName => System.IO.Path.GetFileName(Entry.OriginalPath);
        public string Detection => Entry.Detection;
        public string When => Entry.QuarantinedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private sealed class DefenderEventRow(DefenderOperationalEvent item)
    {
        public string When => item.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Severity => item.Severity.ToString();
        public string Title => item.Title;
        public string Detail => item.ResourcePathRedacted
            ? item.Detail + " · resource path redacted"
            : item.Detail;
    }

    private sealed class UpdateJournalRow(UpdatePlanningJournalEntry entry)
    {
        public string When => entry.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Phase => entry.Phase.ToString();
        public string Detail => entry.Detail;
    }
}
