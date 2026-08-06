using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Monitoring;

namespace Soltex.App.Views;

public partial class MonitoringView : UserControl
{
    private readonly BoundedTelemetryHistory _cpuHistory = new(72);
    private readonly BoundedTelemetryHistory _memoryHistory = new(72);
    private readonly BoundedTelemetryHistory _networkHistory = new(72, 0, double.MaxValue);

    public MonitoringView()
    {
        InitializeComponent();
    }

    public void UpdateSnapshot(SystemTelemetrySnapshot snapshot)
    {
        if (snapshot.CpuPercent is double cpu)
        {
            CpuHistoryChart.Values = _cpuHistory.Add(cpu);
        }

        if (snapshot.Memory is MemoryTelemetry memory)
        {
            MemoryHistoryChart.Values = _memoryHistory.Add(memory.UsedPercent);
            MemoryValueText.Text = TelemetryDisplay.Percent(memory.UsedPercent);
            MemoryCapacityText.Text =
                $"{TelemetryDisplay.Bytes(memory.UsedBytes)} used · {TelemetryDisplay.Bytes(memory.TotalBytes)} physical";
        }
        else
        {
            MemoryValueText.Text = "—";
            MemoryCapacityText.Text = "Physical-memory status unavailable";
        }

        CpuValueText.Text = TelemetryDisplay.Percent(snapshot.CpuPercent);
        CpuHistoryChart.Values ??= _cpuHistory.CreateSnapshot();
        MemoryHistoryChart.Values ??= _memoryHistory.CreateSnapshot();
        RenderNetwork(snapshot.Network);

        Brush stateBrush = (Brush)FindResource(snapshot.State == TelemetryObservationState.Current
            ? "SignalBrush"
            : snapshot.State == TelemetryObservationState.Partial ? "WarningBrush" : "DangerBrush");
        MonitorStateDot.Fill = stateBrush;
        MonitorStateText.Foreground = stateBrush;
        MonitorStateText.Text = TelemetryDisplay.State(snapshot.State);

        VolumeRow[] volumes = snapshot.Volumes.Select(volume => new VolumeRow(volume)).ToArray();
        VolumeItems.ItemsSource = volumes;
        VolumeCountText.Text = volumes.Length == 1 ? "1 observed" : $"{volumes.Length} observed";
        NoVolumesText.Visibility = volumes.Length == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        ProcessRow[] processes = snapshot.Processes.Select(process => new ProcessRow(process)).ToArray();
        ProcessGrid.ItemsSource = processes;
        ProcessCountText.Text =
            $"{processes.Length} rows · {snapshot.InaccessibleProcessCount} inaccessible";
        MonitoringProvenanceText.Text =
            $"{snapshot.Provenance} · captured {snapshot.CapturedAtUtc.ToLocalTime():T} · " +
            $"provider {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            string.Join(" · ", snapshot.Limitations);
    }

    public void ShowUnavailable()
    {
        Brush danger = (Brush)FindResource("DangerBrush");
        MonitorStateDot.Fill = danger;
        MonitorStateText.Foreground = danger;
        MonitorStateText.Text = "UNAVAILABLE";
        NetworkCoverageText.Text = "UNAVAILABLE";
        NetworkCoverageText.Foreground = danger;
        MonitoringProvenanceText.Text =
            "The bounded Windows telemetry provider could not complete a sample. No values were synthesized.";
    }

    public void ShowStale()
    {
        Brush warning = (Brush)FindResource("WarningBrush");
        MonitorStateDot.Fill = warning;
        MonitorStateText.Foreground = warning;
        MonitorStateText.Text = TelemetryDisplay.State(TelemetryObservationState.Stale);
        NetworkCoverageText.Text = "STALE";
        NetworkCoverageText.Foreground = warning;
        MonitoringProvenanceText.Text =
            "Last confirmed values are retained while the bounded provider retries; no new values were synthesized.";
    }

    private void RenderNetwork(NetworkTelemetry? network)
    {
        if (network is null)
        {
            NetworkTotalText.Text = "—";
            NetworkReceiveText.Text = "Receive unavailable";
            NetworkSendText.Text = "Send unavailable";
            NetworkInterfaceCountText.Text = "0 stable interfaces";
            NetworkInterfaceItems.ItemsSource = Array.Empty<NetworkInterfaceRow>();
            NetworkHistoryChart.Values ??= _networkHistory.CreateSnapshot();
            NetworkCoverageText.Text = "UNAVAILABLE";
            NetworkCoverageText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        NetworkHistoryChart.Values = _networkHistory.Add(network.TotalBytesPerSecond);
        NetworkTotalText.Text = TelemetryDisplay.BytesPerSecond(network.TotalBytesPerSecond);
        NetworkReceiveText.Text = "↓ " + TelemetryDisplay.BytesPerSecond(network.ReceiveBytesPerSecond);
        NetworkSendText.Text = "↑ " + TelemetryDisplay.BytesPerSecond(network.SendBytesPerSecond);
        NetworkInterfaceCountText.Text = network.Interfaces.Count == 1
            ? "1 active interface"
            : $"{network.Interfaces.Count} active interfaces";
        NetworkInterfaceItems.ItemsSource = network.Interfaces
            .Take(4)
            .Select(item => new NetworkInterfaceRow(item))
            .ToArray();
        NetworkCoverageText.Text = "SAMPLED";
        NetworkCoverageText.Foreground = (Brush)FindResource("SignalBrush");
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
