using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.DeviceFabric;
using Soltex.Monitoring;

namespace Soltex.App.Views;

public partial class HomeView : UserControl
{
    private readonly BoundedTelemetryHistory _cpuHistory = new(48);

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
    }

    public void ShowUnavailable(LocalDeviceObservation device)
    {
        HomeStateDot.Fill = (Brush)FindResource("DangerBrush");
        HomeStateText.Foreground = (Brush)FindResource("DangerBrush");
        HomeStateText.Text = "UNAVAILABLE";
        HomeCapturedText.Text = "The bounded Windows telemetry provider could not complete a sample.";
        MachineNameText.Text = device.DisplayName;
        MachineOsText.Text = $"{device.OperatingSystem} · {device.OperatingSystemArchitecture}";
    }

    public void ShowStale()
    {
        HomeStateDot.Fill = (Brush)FindResource("WarningBrush");
        HomeStateText.Foreground = (Brush)FindResource("WarningBrush");
        HomeStateText.Text = TelemetryDisplay.State(TelemetryObservationState.Stale);
        HomeCapturedText.Text = "Last confirmed values retained while the bounded provider retries.";
    }
}
