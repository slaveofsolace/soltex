using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Monitoring;

namespace Soltex.App.Views;

internal sealed class ProcessActionCompletedEventArgs : EventArgs
{
    internal ProcessActionCompletedEventArgs(ProcessActionResult result)
    {
        Result = result;
    }

    internal ProcessActionResult Result { get; }
}

public partial class MonitoringView : UserControl
{
    private const int HistoryCapacity = 72;

    private readonly BoundedTelemetryHistory _cpuHistory = new(HistoryCapacity);
    private readonly BoundedTelemetryHistory _memoryHistory = new(HistoryCapacity);
    private readonly BoundedTelemetryHistory _networkReceiveHistory = new(HistoryCapacity, 0, double.MaxValue);
    private readonly BoundedTelemetryHistory _networkSendHistory = new(HistoryCapacity, 0, double.MaxValue);
    private bool _detailsVisible;
    private bool _isRefreshingProcesses;
    private bool _processActionBusy;
    private ProcessActionTicket? _pendingForceTicket;

    public MonitoringView()
    {
        InitializeComponent();

        // Percentage charts label their bounds directly; throughput needs the
        // byte formatter so an autoscaled axis stays readable.
        CpuHistoryChart.ScaleLabelFormatter = FormatPercentBound;
        MemoryHistoryChart.ScaleLabelFormatter = FormatPercentBound;
        NetworkHistoryChart.ScaleLabelFormatter = FormatRateBound;
    }

    internal event EventHandler<ProcessActionCompletedEventArgs>? ProcessActionCompleted;

    private void MonitoringDetails_Click(object sender, RoutedEventArgs e) =>
        SetDetailsVisible(!_detailsVisible);

    internal void SetDetailsVisible(bool visible)
    {
        _detailsVisible = visible;
        MonitoringDetailsPanel.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        MonitoringDetailsButton.Content = _detailsVisible ? "Hide system detail" : "Show system detail";
        MonitoringDetailsButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            _detailsVisible
                ? "Hide storage, provider, and process details"
                : "Show storage, provider, and process details");
    }

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        ProcessRow? selected = ProcessGrid.SelectedItem as ProcessRow;
        if (_pendingForceTicket is not null &&
            (selected is null ||
             selected.ProcessId != _pendingForceTicket.ProcessId ||
             !string.Equals(selected.Name, _pendingForceTicket.ExpectedName, StringComparison.OrdinalIgnoreCase)))
        {
            _pendingForceTicket = null;
            HideProcessAction();
        }

        UpdateProcessActionControls(selected);
    }

    private async void EndTask_Click(object sender, RoutedEventArgs e)
    {
        if (_processActionBusy || ProcessGrid.SelectedItem is not ProcessRow selected)
        {
            return;
        }

        SetProcessActionBusy(true);
        HideProcessAction();
        ProcessActionResult result = await ProcessActionService.RequestCloseAsync(
            new ProcessActionRequest(selected.ProcessId, selected.Name));
        _pendingForceTicket = result.Ticket;
        ShowProcessActionResult(result);
        ProcessActionCompleted?.Invoke(this, new ProcessActionCompletedEventArgs(result));
        SetProcessActionBusy(false);
    }

    private async void ForceStop_Click(object sender, RoutedEventArgs e)
    {
        if (_processActionBusy || _pendingForceTicket is not ProcessActionTicket ticket)
        {
            return;
        }

        SetProcessActionBusy(true);
        ProcessActionResult result = await ProcessActionService.ForceStopAsync(ticket);
        _pendingForceTicket = null;
        ShowProcessActionResult(result);
        ProcessActionCompleted?.Invoke(this, new ProcessActionCompletedEventArgs(result));
        SetProcessActionBusy(false);
    }

    private void CancelEndTask_Click(object sender, RoutedEventArgs e)
    {
        _pendingForceTicket = null;
        HideProcessAction();
        UpdateProcessActionControls(ProcessGrid.SelectedItem as ProcessRow);
    }

    private void SetProcessActionBusy(bool busy)
    {
        _processActionBusy = busy;
        ProcessGrid.IsEnabled = !busy;
        EndTaskButton.Content = busy ? "Working…" : "End task";
        UpdateProcessActionControls(ProcessGrid.SelectedItem as ProcessRow);
    }

    private void UpdateProcessActionControls(ProcessRow? selected)
    {
        EndTaskButton.IsEnabled =
            !_processActionBusy &&
            _pendingForceTicket is null &&
            selected is not null;
        EndTaskButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.HelpTextProperty,
            selected is null
                ? "Select a process row first."
                : $"Request a graceful close for {selected.Name}, PID {selected.ProcessId}.");
    }

    private void ShowProcessActionResult(ProcessActionResult result)
    {
        ProcessActionText.Text = result.Message;
        ProcessActionText.Foreground = (Brush)FindResource(result.Status switch
        {
            ProcessActionStatus.Closed or
            ProcessActionStatus.AlreadyExited or
            ProcessActionStatus.ForceStopped => "SignalBrush",
            ProcessActionStatus.NeedsForceConfirmation => "WarningBrush",
            _ => "DangerBrush"
        });
        bool requiresForce =
            result.Status == ProcessActionStatus.NeedsForceConfirmation &&
            result.Ticket is not null;
        ForceStopButton.Visibility = requiresForce ? Visibility.Visible : Visibility.Collapsed;
        CancelEndTaskButton.Content = requiresForce ? "Cancel" : "Dismiss";
        ProcessActionPanel.Visibility = Visibility.Visible;
        UpdateProcessActionControls(ProcessGrid.SelectedItem as ProcessRow);
    }

    private void HideProcessAction()
    {
        ProcessActionPanel.Visibility = Visibility.Collapsed;
        ForceStopButton.Visibility = Visibility.Collapsed;
        CancelEndTaskButton.Content = "Dismiss";
        ProcessActionText.Text = string.Empty;
    }

    private static string FormatPercentBound(double value) =>
        value.ToString("F0", CultureInfo.CurrentCulture) + "%";

    private static string FormatRateBound(double value) =>
        TelemetryDisplay.BytesPerSecond((long)Math.Clamp(value, 0, long.MaxValue));

    public void UpdateSnapshot(SystemTelemetrySnapshot snapshot)
    {
        RenderCpu(snapshot.CpuPercent);
        RenderMemory(snapshot.Memory);
        RenderNetwork(snapshot.Network);
        RenderState(snapshot.State);
        RenderVolumes(snapshot.Volumes);
        RenderProcesses(snapshot);
    }

    public void ShowUnavailable()
    {
        Brush danger = (Brush)FindResource("DangerBrush");
        SetStatePill(danger, "UNAVAILABLE");
        NetworkCoverageText.Text = "UNAVAILABLE";
        NetworkCoverageText.Foreground = danger;
        MonitoringProvenanceText.Text =
            "The bounded Windows telemetry provider could not complete a sample. No values were synthesized.";
    }

    public void ShowStale()
    {
        Brush warning = (Brush)FindResource("WarningBrush");
        SetStatePill(warning, TelemetryDisplay.State(TelemetryObservationState.Stale));
        NetworkCoverageText.Text = "STALE";
        NetworkCoverageText.Foreground = warning;
        MonitoringProvenanceText.Text =
            "Last confirmed values are retained while the bounded provider retries; no new values were synthesized.";
    }

    private void RenderCpu(double? cpuPercent)
    {
        ReadOnlyCollection<double> history = cpuPercent is double cpu
            ? _cpuHistory.Add(cpu)
            : _cpuHistory.CreateSnapshot();
        CpuHistoryChart.Values = history;
        CpuValueText.Text = TelemetryDisplay.Percent(cpuPercent);
        CpuStatsText.Text = DescribePercentSeries(history);
        CpuWindowText.Text = DescribeWindow(history.Count);
    }

    private void RenderMemory(MemoryTelemetry? memory)
    {
        if (memory is null)
        {
            MemoryHistoryChart.Values = _memoryHistory.CreateSnapshot();
            MemoryValueText.Text = "—";
            MemoryCapacityText.Text = "Physical-memory status unavailable";
            MemoryStatsText.Text = "no samples";
            SetCommitUnavailable();
            return;
        }

        ReadOnlyCollection<double> history = _memoryHistory.Add(memory.UsedPercent);
        MemoryHistoryChart.Values = history;
        MemoryValueText.Text = TelemetryDisplay.Percent(memory.UsedPercent);
        MemoryCapacityText.Text =
            $"{TelemetryDisplay.Bytes(memory.UsedBytes)} of {TelemetryDisplay.Bytes(memory.TotalBytes)}";
        MemoryStatsText.Text = DescribePercentSeries(history);

        if (memory.HasCommitCharge)
        {
            CommitProgress.Value = memory.CommitUsedPercent;
            CommitText.Text =
                $"{TelemetryDisplay.Bytes(memory.CommitUsedBytes)} / {TelemetryDisplay.Bytes(memory.CommitLimitBytes)}";
        }
        else
        {
            SetCommitUnavailable();
        }
    }

    private void SetCommitUnavailable()
    {
        CommitProgress.Value = 0;
        CommitText.Text = "unavailable";
    }

    private void RenderNetwork(NetworkTelemetry? network)
    {
        if (network is null)
        {
            NetworkHistoryChart.Values = _networkReceiveHistory.CreateSnapshot();
            NetworkHistoryChart.ComparisonValues = _networkSendHistory.CreateSnapshot();
            NetworkTotalText.Text = "—";
            NetworkReceiveText.Text = "Receive unavailable";
            NetworkSendText.Text = "Send unavailable";
            NetworkInterfaceCountText.Text = "0 stable interfaces";
            NetworkInterfaceItems.ItemsSource = Array.Empty<NetworkInterfaceRow>();
            NoInterfacesText.Visibility = Visibility.Visible;
            NetworkScaleText.Text = "No stable interface sample completed";
            NetworkCoverageText.Text = "UNAVAILABLE";
            NetworkCoverageText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        ReadOnlyCollection<double> receive = _networkReceiveHistory.Add(network.ReceiveBytesPerSecond);
        ReadOnlyCollection<double> send = _networkSendHistory.Add(network.SendBytesPerSecond);
        NetworkHistoryChart.Values = receive;
        NetworkHistoryChart.ComparisonValues = send;

        NetworkTotalText.Text = TelemetryDisplay.BytesPerSecond(network.TotalBytesPerSecond);
        NetworkReceiveText.Text = "↓ " + TelemetryDisplay.BytesPerSecond(network.ReceiveBytesPerSecond);
        NetworkSendText.Text = "↑ " + TelemetryDisplay.BytesPerSecond(network.SendBytesPerSecond);
        NetworkInterfaceCountText.Text = network.Interfaces.Count == 1
            ? "1 active interface"
            : $"{network.Interfaces.Count} active interfaces";

        NetworkInterfaceRow[] rows = network.Interfaces
            .Take(4)
            .Select(item => new NetworkInterfaceRow(item))
            .ToArray();
        NetworkInterfaceItems.ItemsSource = rows;
        NoInterfacesText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        double peak = Math.Max(Maximum(receive), Maximum(send));
        NetworkScaleText.Text =
            $"peak {TelemetryDisplay.BytesPerSecond((long)Math.Min(peak, long.MaxValue))} · {DescribeWindow(receive.Count)}";
        NetworkCoverageText.Text = "SAMPLED";
        NetworkCoverageText.Foreground = (Brush)FindResource("SignalBrush");
    }

    private void RenderState(TelemetryObservationState state)
    {
        Brush stateBrush = (Brush)FindResource(state switch
        {
            TelemetryObservationState.Current => "SignalBrush",
            TelemetryObservationState.Partial => "WarningBrush",
            _ => "DangerBrush"
        });
        SetStatePill(stateBrush, TelemetryDisplay.State(state));
    }

    private void SetStatePill(Brush brush, string text)
    {
        MonitorStateDot.Fill = brush;
        MonitorStateText.Foreground = brush;
        MonitorStateText.Text = text;
    }

    private void RenderVolumes(IReadOnlyList<StorageVolumeTelemetry> snapshotVolumes)
    {
        VolumeRow[] volumes = snapshotVolumes.Select(volume => new VolumeRow(volume)).ToArray();
        VolumeItems.ItemsSource = volumes;
        VolumeCountText.Text = volumes.Length == 1 ? "1 observed" : $"{volumes.Length} observed";
        NoVolumesText.Visibility = volumes.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderProcesses(SystemTelemetrySnapshot snapshot)
    {
        int? selectedProcessId = (ProcessGrid.SelectedItem as ProcessRow)?.ProcessId;
        ProcessRow[] processes = snapshot.Processes.Select(process => new ProcessRow(process)).ToArray();
        _isRefreshingProcesses = true;
        try
        {
            ProcessGrid.ItemsSource = processes;
            ProcessGrid.SelectedItem = selectedProcessId is int processId
                ? processes.FirstOrDefault(process => process.ProcessId == processId)
                : null;
        }
        finally
        {
            _isRefreshingProcesses = false;
        }

        ProcessRow? selected = ProcessGrid.SelectedItem as ProcessRow;
        if (_pendingForceTicket is not null &&
            (selected is null || selected.ProcessId != _pendingForceTicket.ProcessId))
        {
            _pendingForceTicket = null;
            HideProcessAction();
        }
        UpdateProcessActionControls(selected);
        ProcessCountText.Text =
            $"{processes.Length} rows · {snapshot.InaccessibleProcessCount} inaccessible";
        MonitoringProvenanceText.Text =
            $"{snapshot.Provenance} · captured {snapshot.CapturedAtUtc.ToLocalTime():T} · " +
            $"provider {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            string.Join(" · ", snapshot.Limitations);
    }

    private static string DescribeWindow(int sampleCount) =>
        sampleCount == 1
            ? $"1 of {HistoryCapacity} samples"
            : $"{sampleCount} of {HistoryCapacity} samples";

    private static string DescribePercentSeries(ReadOnlyCollection<double> samples)
    {
        if (samples.Count == 0)
        {
            return "awaiting samples";
        }

        double minimum = samples[0];
        double maximum = samples[0];
        double total = 0;
        foreach (double sample in samples)
        {
            minimum = Math.Min(minimum, sample);
            maximum = Math.Max(maximum, sample);
            total += sample;
        }

        string Format(double value) => value.ToString("F0", CultureInfo.CurrentCulture);
        return $"min {Format(minimum)} · avg {Format(total / samples.Count)} · peak {Format(maximum)}";
    }

    private static double Maximum(ReadOnlyCollection<double> samples)
    {
        double maximum = 0;
        foreach (double sample in samples)
        {
            maximum = Math.Max(maximum, sample);
        }

        return maximum;
    }

    private sealed class VolumeRow(StorageVolumeTelemetry volume)
    {
        public string Name => volume.Name;

        public double UsedPercent => volume.UsedPercent;

        public string Detail =>
            $"{TelemetryDisplay.Bytes(volume.UsedBytes)} / {TelemetryDisplay.Bytes(volume.TotalBytes)}";
    }

    private sealed class NetworkInterfaceRow(NetworkInterfaceTelemetry networkInterface)
    {
        public string Name => networkInterface.Name;

        public string Type => networkInterface.InterfaceType;

        public string Rate => TelemetryDisplay.BytesPerSecond(networkInterface.TotalBytesPerSecond);

        public string Split =>
            $"↓{TelemetryDisplay.BytesPerSecond(networkInterface.ReceiveBytesPerSecond)} " +
            $"↑{TelemetryDisplay.BytesPerSecond(networkInterface.SendBytesPerSecond)}";
    }

    private sealed class ProcessRow(ProcessTelemetry process)
    {
        public string Name => process.Name;

        public int ProcessId => process.ProcessId;

        public string Cpu => process.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + "%";

        public string Memory => TelemetryDisplay.Bytes(process.WorkingSetBytes);

        public int Threads => process.ThreadCount;
    }
}
