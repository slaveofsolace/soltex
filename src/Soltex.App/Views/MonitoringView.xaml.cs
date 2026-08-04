using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Monitoring;

namespace Soltex.App.Views;

public partial class MonitoringView : UserControl
{
    private const int MaximumHistoryCount = 72;
    private readonly Queue<double> _cpuHistory = [];
    private readonly Queue<double> _memoryHistory = [];

    public MonitoringView()
    {
        InitializeComponent();
    }

    public void UpdateSnapshot(SystemTelemetrySnapshot snapshot)
    {
        if (snapshot.CpuPercent is double cpu)
        {
            AddHistory(_cpuHistory, cpu);
        }

        if (snapshot.Memory is MemoryTelemetry memory)
        {
            AddHistory(_memoryHistory, memory.UsedPercent);
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
        CpuHistoryChart.Values = _cpuHistory.ToArray();
        MemoryHistoryChart.Values = _memoryHistory.ToArray();

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
        MonitoringProvenanceText.Text =
            "The bounded Windows telemetry provider could not complete a sample. No values were synthesized.";
    }

    private static void AddHistory(Queue<double> history, double value)
    {
        history.Enqueue(Math.Clamp(value, 0, 100));
        while (history.Count > MaximumHistoryCount)
        {
            history.Dequeue();
        }
    }

    private sealed class VolumeRow(StorageVolumeTelemetry volume)
    {
        public string Name => volume.Name;

        public double UsedPercent => volume.UsedPercent;

        public string Detail =>
            $"{TelemetryDisplay.Bytes(volume.UsedBytes)} / {TelemetryDisplay.Bytes(volume.TotalBytes)}";
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
