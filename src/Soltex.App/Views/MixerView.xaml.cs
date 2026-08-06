using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Audio;

namespace Soltex.App.Views;

public partial class MixerView : UserControl
{
    public MixerView()
    {
        InitializeComponent();
    }

    public void UpdateSnapshot(AudioEndpointSnapshot snapshot)
    {
        RenderState(snapshot.State);

        EndpointRow[] playback = snapshot.Render.Select(endpoint => new EndpointRow(endpoint)).ToArray();
        EndpointRow[] recording = snapshot.Capture.Select(endpoint => new EndpointRow(endpoint)).ToArray();
        PlaybackItems.ItemsSource = playback;
        RecordingItems.ItemsSource = recording;
        NoPlaybackText.Visibility = playback.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecordingText.Visibility = recording.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        MixerProvenanceText.Text =
            $"{snapshot.Provenance} · captured {snapshot.CapturedAtUtc.ToLocalTime():T} · " +
            $"provider {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            $"{snapshot.InaccessibleEndpointCount} inaccessible · " +
            string.Join(" · ", snapshot.Limitations);
    }

    public void ShowUnavailable()
    {
        Brush danger = (Brush)FindResource("DangerBrush");
        SetStatePill(danger, "UNAVAILABLE");
        MixerProvenanceText.Text = "Windows Core Audio could not be reached. No values were synthesized.";
    }

    private void RenderState(AudioObservationState state)
    {
        Brush brush = (Brush)FindResource(state switch
        {
            AudioObservationState.Current => "SignalBrush",
            AudioObservationState.Partial => "WarningBrush",
            _ => "DangerBrush"
        });
        SetStatePill(brush, state switch
        {
            AudioObservationState.Current => "CURRENT",
            AudioObservationState.Partial => "PARTIAL",
            _ => "UNAVAILABLE"
        });
    }

    private void SetStatePill(Brush brush, string text)
    {
        MixerStateDot.Fill = brush;
        MixerStateText.Foreground = brush;
        MixerStateText.Text = text;
    }

    private sealed class EndpointRow(AudioEndpoint endpoint)
    {
        public string Name => endpoint.Name;

        public string StateText => endpoint.State switch
        {
            AudioEndpointState.Active => "ACTIVE",
            AudioEndpointState.Disabled => "DISABLED",
            AudioEndpointState.NotPresent => "NOT PRESENT",
            AudioEndpointState.Unplugged => "UNPLUGGED",
            _ => "UNKNOWN"
        };

        public Visibility DefaultVisibility => endpoint.IsDefault ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MuteVisibility => endpoint.IsMuted == true ? Visibility.Visible : Visibility.Collapsed;

        public double VolumePercent => endpoint.VolumePercent ?? 0;

        public string VolumeText => endpoint.VolumePercent is double percent ? $"{percent:F0}%" : "—";
    }
}
