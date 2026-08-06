using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.DeviceFabric;
using Soltex.Monitoring;

namespace Soltex.App.Views;

public partial class HomeView : UserControl
{
    private readonly BoundedTelemetryHistory _cpuHistory = new(48);
    private readonly BoundedTelemetryHistory _networkHistory = new(48, 0, double.MaxValue);

    public HomeView()
    {
        InitializeComponent();
    }

    public void UpdateSnapshot(SystemTelemetrySnapshot snapshot, LocalDeviceObservation device)
    {
        if (snapshot.CpuPercent is double cpu)
        {
            CpuSparkline.Values = _cpuHistory.Add(cpu);
        }

        CpuHeroValue.Text = TelemetryDisplay.Percent(snapshot.CpuPercent);
        CpuSparkline.Values ??= _cpuHistory.CreateSnapshot();
        HomeCapturedText.Text =
            $"{snapshot.Provenance} · {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · {snapshot.CapturedAtUtc.ToLocalTime():t}";

        Brush stateBrush = (Brush)FindResource(snapshot.State == TelemetryObservationState.Current
            ? "SignalBrush"
            : snapshot.State == TelemetryObservationState.Partial ? "WarningBrush" : "DangerBrush");
        HomeStateDot.Fill = stateBrush;
        HomeStateText.Foreground = stateBrush;
        HomeStateText.Text = TelemetryDisplay.State(snapshot.State);

        MachineNameText.Text = device.DisplayName;
        MachineOsText.Text = $"{device.OperatingSystem} · {device.OperatingSystemArchitecture}";

        if (snapshot.Memory is MemoryTelemetry memory)
        {
            MemoryProgress.Value = memory.UsedPercent;
            MemoryText.Text = TelemetryDisplay.Percent(memory.UsedPercent);
        }
        else
        {
            MemoryProgress.Value = 0;
            MemoryText.Text = "Unavailable";
        }

        StorageVolumeTelemetry? volume = snapshot.Volumes.Count > 0 ? snapshot.Volumes[0] : null;
        if (volume is not null)
        {
            StorageNameText.Text = volume.Name;
            StorageProgress.Value = volume.UsedPercent;
            StorageText.Text = TelemetryDisplay.Percent(volume.UsedPercent);
        }
        else
        {
            StorageNameText.Text = "Storage";
            StorageProgress.Value = 0;
            StorageText.Text = "Unavailable";
        }

        ProcessTelemetry? topProcess = snapshot.Processes.Count > 0 ? snapshot.Processes[0] : null;
        TopProcessNameText.Text = topProcess?.Name ?? "No process sample available";
        TopProcessValueText.Text = topProcess is null
            ? "—"
            : topProcess.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + "%";

        RenderNetwork(snapshot.Network);
    }

    public void ShowUnavailable(LocalDeviceObservation device)
    {
        HomeStateDot.Fill = (Brush)FindResource("DangerBrush");
        HomeStateText.Foreground = (Brush)FindResource("DangerBrush");
        HomeStateText.Text = "UNAVAILABLE";
        HomeCapturedText.Text = "The bounded Windows telemetry provider could not complete a sample.";
        MachineNameText.Text = device.DisplayName;
        MachineOsText.Text = $"{device.OperatingSystem} · {device.OperatingSystemArchitecture}";
        NetworkStatusText.Text = "UNAVAILABLE";
        NetworkStatusText.Foreground = (Brush)FindResource("DangerBrush");
    }

    public void ShowStale()
    {
        HomeStateDot.Fill = (Brush)FindResource("WarningBrush");
        HomeStateText.Foreground = (Brush)FindResource("WarningBrush");
        HomeStateText.Text = TelemetryDisplay.State(TelemetryObservationState.Stale);
        HomeCapturedText.Text = "Last confirmed values retained while the bounded provider retries.";
        NetworkStatusText.Text = "STALE";
        NetworkStatusText.Foreground = (Brush)FindResource("WarningBrush");
    }

    private void RenderNetwork(NetworkTelemetry? network)
    {
        if (network is null)
        {
            NetworkRateText.Text = "Unavailable";
            NetworkDetailText.Text = "No stable active-interface sample completed";
            NetworkStatusText.Text = "UNAVAILABLE";
            NetworkStatusText.Foreground = (Brush)FindResource("WarningBrush");
            NetworkSparkline.Values ??= _networkHistory.CreateSnapshot();
            return;
        }

        NetworkSparkline.Values = _networkHistory.Add(network.TotalBytesPerSecond);
        NetworkRateText.Text = TelemetryDisplay.BytesPerSecond(network.TotalBytesPerSecond);
        NetworkDetailText.Text =
            $"↓ {TelemetryDisplay.BytesPerSecond(network.ReceiveBytesPerSecond)} · " +
            $"↑ {TelemetryDisplay.BytesPerSecond(network.SendBytesPerSecond)} · " +
            $"{network.Interfaces.Count} active interface{(network.Interfaces.Count == 1 ? string.Empty : "s")}";
        NetworkStatusText.Text = "LIVE";
        NetworkStatusText.Foreground = (Brush)FindResource("SignalBrush");
    }
}
