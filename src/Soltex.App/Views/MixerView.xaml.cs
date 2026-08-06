using System.Globalization;
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

        EndpointPalette palette = new(
            Active: (Brush)FindResource("SignalBrush"),
            Attention: (Brush)FindResource("WarningBrush"),
            Quiet: (Brush)FindResource("QuietTextBrush"),
            Text: (Brush)FindResource("TextBrush"),
            Track: (Brush)FindResource("TrackBrush"));

        EndpointRow[] playback = BuildRows(snapshot.Render, palette);
        EndpointRow[] recording = BuildRows(snapshot.Capture, palette);
        PlaybackItems.ItemsSource = playback;
        RecordingItems.ItemsSource = recording;
        NoPlaybackText.Visibility = playback.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecordingText.Visibility = recording.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        PlaybackCountText.Text = DescribeCount(playback);
        RecordingCountText.Text = DescribeCount(recording);

        int active = snapshot.Endpoints.Count(endpoint => endpoint.State == AudioEndpointState.Active);
        ActiveCountText.Text = active.ToString(CultureInfo.CurrentCulture);
        TotalCountText.Text = snapshot.Endpoints.Count == 1
            ? "of 1 endpoint"
            : $"of {snapshot.Endpoints.Count} endpoints";
        DefaultPlaybackText.Text = DescribeDefault(snapshot.Render);
        DefaultRecordingText.Text = DescribeDefault(snapshot.Capture);
        StateBreakdownText.Text = DescribeStates(snapshot.Endpoints);

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

    // Connected endpoints first, then the default, then by name. A machine can
    // report dozens of absent endpoints; the ones in use must not be buried.
    private static EndpointRow[] BuildRows(IEnumerable<AudioEndpoint> endpoints, EndpointPalette palette) =>
        endpoints
            .OrderBy(endpoint => StateRank(endpoint.State))
            .ThenByDescending(endpoint => endpoint.IsDefault)
            .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(endpoint => new EndpointRow(endpoint, palette))
            .ToArray();

    private static int StateRank(AudioEndpointState state) => state switch
    {
        AudioEndpointState.Active => 0,
        AudioEndpointState.Unplugged => 1,
        AudioEndpointState.Disabled => 2,
        AudioEndpointState.NotPresent => 3,
        _ => 4
    };

    private static string DescribeCount(EndpointRow[] rows)
    {
        int active = rows.Count(row => row.IsActive);
        return $"{active}/{rows.Length} active";
    }

    private static string DescribeDefault(IEnumerable<AudioEndpoint> endpoints) =>
        endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Name ?? "Not reported";

    private static string DescribeStates(IEnumerable<AudioEndpoint> endpoints)
    {
        var counts = endpoints
            .GroupBy(endpoint => endpoint.State)
            .OrderBy(group => StateRank(group.Key))
            .Select(group => $"{group.Count()} {StateLabel(group.Key).ToLowerInvariant()}")
            .ToArray();
        return counts.Length == 0 ? "no endpoints reported" : string.Join(" · ", counts);
    }

    private static string StateLabel(AudioEndpointState state) => state switch
    {
        AudioEndpointState.Active => "ACTIVE",
        AudioEndpointState.Disabled => "DISABLED",
        AudioEndpointState.NotPresent => "NOT PRESENT",
        AudioEndpointState.Unplugged => "UNPLUGGED",
        _ => "UNKNOWN"
    };

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

    private sealed record EndpointPalette(Brush Active, Brush Attention, Brush Quiet, Brush Text, Brush Track);

    private sealed class EndpointRow
    {
        private readonly AudioEndpoint _endpoint;
        private readonly EndpointPalette _palette;

        internal EndpointRow(AudioEndpoint endpoint, EndpointPalette palette)
        {
            _endpoint = endpoint;
            _palette = palette;
        }

        internal bool IsActive => _endpoint.State == AudioEndpointState.Active;

        public string Name => _endpoint.Name;

        public string Tooltip => $"{_endpoint.Name} — {StateText}";

        public string StateText => StateLabel(_endpoint.State);

        public Brush StateBrush => _endpoint.State switch
        {
            AudioEndpointState.Active => _palette.Active,
            AudioEndpointState.Unplugged => _palette.Attention,
            _ => _palette.Quiet
        };

        public double RowOpacity => IsActive ? 1.0 : 0.62;

        public Visibility DefaultVisibility => _endpoint.IsDefault ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MuteVisibility => _endpoint.IsMuted == true ? Visibility.Visible : Visibility.Collapsed;

        // A disconnected endpoint still reports a stored level. Showing it as a
        // confident green bar implies the device is doing something, so the
        // level is withheld unless the endpoint is actually active.
        public double VolumePercent => IsActive ? _endpoint.VolumePercent ?? 0 : 0;

        public Brush VolumeBrush => IsActive && _endpoint.IsMuted != true
            ? _palette.Active
            : _palette.Track;

        public Brush VolumeTextBrush => IsActive ? _palette.Text : _palette.Quiet;

        public string VolumeText => IsActive && _endpoint.VolumePercent is double percent
            ? $"{percent:F0}%"
            : "—";

        public string VolumeAccessibleName => IsActive && _endpoint.VolumePercent is double percent
            ? $"{_endpoint.Name} volume {percent:F0} percent"
            : $"{_endpoint.Name} level unavailable";
    }
}
