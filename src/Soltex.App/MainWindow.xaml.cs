using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Soltex.RemoteAssist;
using Soltex.Security;
using Soltex.Update;

namespace Soltex.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Window lifecycle; Closed cancels and disposes every owned resource.")]
public partial class MainWindow : Window
{
    private readonly SecurityRuntime _runtime = SecurityRuntime.CreateDefault();
    private readonly ObservableCollection<QuarantineRow> _quarantineRows = [];
    private readonly ObservableCollection<DefenderEventRow> _defenderEventRows = [];
    private readonly ObservableCollection<UpdateJournalRow> _updateJournalRows = [];
    private readonly UpdatePlanningJournal _updateJournal;
    private readonly string _updateStagingRoot;
    private CancellationTokenSource? _operationCancellation;
    private ImportFolderMonitor? _importMonitor;
    private ProtectionMonitor? _protectionMonitor;
    private ProtectionMonitorState? _lastMonitorState;
    private RemoteAssistExecutable? _remoteAssistExecutable;
    private AuthenticodeVerificationResult? _remoteAssistTrust;

    public MainWindow()
    {
        InitializeComponent();
        _updateStagingRoot = Path.Combine(_runtime.DataRoot, "update", "staging");
        _updateJournal = new UpdatePlanningJournal(Path.Combine(_runtime.DataRoot, "update", "journal"));
        QuarantineGrid.ItemsSource = _quarantineRows;
        DefenderEventGrid.ItemsSource = _defenderEventRows;
        UpdateJournalGrid.ItemsSource = _updateJournalRows;
        CurrentBuildText.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "development";
        RemotePeerIdInput.TextChanged += RemotePeerId_TextChanged;
        SetRemoteAssistExecutable(RemoteAssistExecutableLocator.FindInstalled());
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _importMonitor = new ImportFolderMonitor(
            _runtime.ImportsPath,
            _runtime.Assessor,
            HandleImportAssessmentAsync);
        _protectionMonitor = new ProtectionMonitor(
            _runtime.Defender,
            new WindowsSecurityChangeMonitor());
        _protectionMonitor.Updated += OnProtectionMonitorUpdated;
        await RefreshAllAsync();
        await RefreshUpdateJournalAsync();
        _protectionMonitor.Start();
        AddActivity("Soltex import guard is active.");
        if (_runtime.DataRootKind == ProductDataRootKind.LegacyCompatibility)
        {
            AddActivity("Soltex is using the existing compatible data location; no files were moved.");
        }

        AddActivity(_protectionMonitor.ChangeNotificationsAvailable
            ? "Windows Security change notifications are active."
            : "Windows Security notifications are unavailable; bounded polling remains active.");
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _operationCancellation?.Cancel();
        if (_importMonitor is not null)
        {
            await _importMonitor.DisposeAsync();
        }

        if (_protectionMonitor is not null)
        {
            _protectionMonitor.Updated -= OnProtectionMonitorUpdated;
            await _protectionMonitor.DisposeAsync();
        }

        _operationCancellation?.Dispose();
        _updateJournal.Dispose();
        _runtime.Dispose();
    }

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
            AddActivity(health.Summary + ".");
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

        HealthDot.Fill = stateBrush;
        HealthTitle.Text = health.Summary;
        string wsc = health.WindowsSecurityCenterHealth.ToString();
        string mode = string.IsNullOrWhiteSpace(health.AMRunningMode)
            ? "mode unavailable"
            : health.AMRunningMode;
        HealthDetail.Text = health.StatusQuerySucceeded
            ? $"WSC {wsc} · Defender {mode} · intelligence " +
              $"{health.AntivirusSignatureVersion ?? "version unavailable"} · checked {health.CheckedAtUtc.ToLocalTime():t}"
            : $"WSC {wsc} · {health.Error ?? "Defender details are managed by the registered provider."}";
        HealthHero.BorderBrush = stateBrush;

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
            EventQueryStatus.Text = "Unavailable";
            EventQueryStatus.Foreground = (Brush)FindResource("WarningBrush");
            AddActivity("Defender activity unavailable: " + result.Error);
            return;
        }

        _defenderEventRows.Clear();
        foreach (DefenderOperationalEvent item in result.Events)
        {
            _defenderEventRows.Add(new DefenderEventRow(item));
        }

        EventQueryStatus.Text = $"{result.Events.Count} events · {result.Duration.TotalMilliseconds:F0} ms";
        EventQueryStatus.Foreground = (Brush)FindResource("MutedBrush");
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
            AddActivity(update.Detail);
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
        if (_operationCancellation is not null)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await action(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AddActivity("Operation cancelled. A Defender scan already accepted by Windows may continue in the background.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            AddActivity("Operation failed: " + exception.Message);
            MessageBox.Show(this, exception.Message, "Soltex Security", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
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
            AddActivity($"{result.Operation}: {result.Message}");
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
            AddActivity($"{System.IO.Path.GetFileName(path)}: {assessment.Verdict}.");

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
                    AddActivity($"{System.IO.Path.GetFileName(path)} moved to quarantine.");
                    await RefreshQuarantineAsync(cancellationToken);
                    return;
                }
            }

            if (File.Exists(path))
            {
                DefenderCommandResult defenderResult = await _runtime.Defender.RunCustomScanAsync(path, cancellationToken);
                AddActivity($"Defender custom scan: {defenderResult.Message}");
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
            AddActivity($"Import guard: {System.IO.Path.GetFileName(assessment.Path)} · {assessment.Verdict}.");
        });
    }

    private void AddActivity(string message)
    {
        ActivityList.Items.Insert(0, $"{DateTimeOffset.Now:t}  {message}");
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
            AddActivity($"Restored {System.IO.Path.GetFileName(restoredPath)}.");
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
            AddActivity($"Deleted {row.FileName} from quarantine.");
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
        AddActivity($"Remote Assist {operation}: {result.Message}");
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
            AddActivity("Remote Assist audit write failed: " + exception.Message);
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
            Brush stateBrush = (Brush)FindResource(reviewRequired ? "WarningBrush" : "SignalBrush");
            UpdateStateDot.Fill = stateBrush;
            UpdateReadinessHero.BorderBrush = stateBrush;
            UpdateStateTitle.Text = reviewRequired ? "Cleanup review required" : "Planner state is clean";
            UpdateStatePill.Text = reviewRequired ? "REVIEW" : "IDLE";
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

    private void MixerNav_Click(object sender, RoutedEventArgs e) => ShowPanel(MixerPanel, MixerNavButton);

    private void ClipsNav_Click(object sender, RoutedEventArgs e) => ShowPanel(ClipsPanel, ClipsNavButton);

    private void SecurityNav_Click(object sender, RoutedEventArgs e) => ShowPanel(SecurityPanel, SecurityNavButton);

    private void RemoteNav_Click(object sender, RoutedEventArgs e) => ShowPanel(RemotePanel, RemoteNavButton);

    private void UpdateNav_Click(object sender, RoutedEventArgs e) => ShowPanel(UpdatePanel, UpdateNavButton);

    internal bool TrySelectRenderSmokePanel(string panelName)
    {
        string normalized = panelName.Trim();
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

        if (string.Equals(normalized, "update", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "updates", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel(UpdatePanel, UpdateNavButton);
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

        return false;
    }

    private void ShowPanel(UIElement panel, System.Windows.Controls.Button selectedButton)
    {
        MixerPanel.Visibility = panel == MixerPanel ? Visibility.Visible : Visibility.Collapsed;
        ClipsPanel.Visibility = panel == ClipsPanel ? Visibility.Visible : Visibility.Collapsed;
        SecurityPanel.Visibility = panel == SecurityPanel ? Visibility.Visible : Visibility.Collapsed;
        RemotePanel.Visibility = panel == RemotePanel ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = panel == UpdatePanel ? Visibility.Visible : Visibility.Collapsed;
        foreach (System.Windows.Controls.Button button in new[]
                 {
                     MixerNavButton,
                     ClipsNavButton,
                     SecurityNavButton,
                     RemoteNavButton,
                     UpdateNavButton
                 })
        {
            button.Background = (Brush)FindResource(button == selectedButton ? "SelectedNavBrush" : "NavRestBrush");
            button.Foreground = button == selectedButton
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush");
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
