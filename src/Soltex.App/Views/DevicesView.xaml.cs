using System.Windows.Controls;
using Soltex.DeviceFabric;

namespace Soltex.App.Views;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        CapabilityItems.ItemsSource = CapabilityCatalog.All.Select(capability => new CapabilityRow(capability)).ToArray();
    }

    public event EventHandler? RemoteAssistRequested;

    public void UpdateObservation(LocalDeviceObservation observation)
    {
        DeviceNameText.Text = observation.DisplayName;
        DeviceOsText.Text = observation.OperatingSystem;
        OsArchitectureText.Text = observation.OperatingSystemArchitecture;
        ProcessArchitectureText.Text = observation.ProcessArchitecture;
        FrameworkText.Text = observation.Framework;
        ObservationStateText.Text = observation.State == LocalDeviceObservationState.Current
            ? "CURRENT LOCAL OBSERVATION"
            : "PARTIAL LOCAL OBSERVATION";
        DeviceProvenanceText.Text =
            $"{observation.Provenance} · captured {observation.CapturedAtUtc.ToLocalTime():G} · " +
            "enrollment NotEnrolled" +
            (observation.Limitations.Count == 0 ? string.Empty : " · " + string.Join(" · ", observation.Limitations));
    }

    private void OpenRemoteAssist_Click(object sender, System.Windows.RoutedEventArgs e) =>
        RemoteAssistRequested?.Invoke(this, EventArgs.Empty);

    private sealed class CapabilityRow(CapabilityDescriptor descriptor)
    {
        public string Name => descriptor.DisplayName;

        public string Description => descriptor.Description;

        public string Effect => descriptor.Effect switch
        {
            CapabilityEffect.ReadOnlyObservation => "READ ONLY",
            CapabilityEffect.StateChangingRequest => "STATE REQUEST",
            _ => "EXTERNAL CLIENT"
        };

        public string Approval => descriptor.ApprovalMode == CapabilityApprovalMode.DeviceLocalReadOnlyPolicy
            ? "LOCAL POLICY"
            : "VISIBLE CONSENT";
    }
}
