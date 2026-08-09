using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Audio;

namespace Soltex.App.Views;

public partial class MixerView : UserControl
{
    private const int PrimaryEndpointLimit = 6;
    private EndpointRow[] _morePlayback = [];
    private EndpointRow[] _moreRecording = [];
    private bool _moreVisible;

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
        EndpointRow[] activePlayback = playback.Where(row => row.IsActive).ToArray();
        EndpointRow[] activeRecording = recording.Where(row => row.IsActive).ToArray();
        EndpointRow[] primaryPlayback = activePlayback.Take(PrimaryEndpointLimit).ToArray();
        EndpointRow[] primaryRecording = activeRecording.Take(PrimaryEndpointLimit).ToArray();
        _morePlayback = playback.Skip(primaryPlayback.Length).ToArray();
        _moreRecording = recording.Skip(primaryRecording.Length).ToArray();

        PlaybackItems.ItemsSource = primaryPlayback;
        RecordingItems.ItemsSource = primaryRecording;
        MorePlaybackItems.ItemsSource = _morePlayback;
        MoreRecordingItems.ItemsSource = _moreRecording;
        NoPlaybackText.Visibility = activePlayback.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecordingText.Visibility = activeRecording.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMorePlaybackText.Visibility = _morePlayback.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMoreRecordingText.Visibility = _moreRecording.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        PlaybackCountText.Text = DescribePrimaryCount(primaryPlayback.Length, activePlayback.Length);
        RecordingCountText.Text = DescribePrimaryCount(primaryRecording.Length, activeRecording.Length);
        MorePlaybackCountText.Text = _morePlayback.Length.ToString(CultureInfo.CurrentCulture);
        MoreRecordingCountText.Text = _moreRecording.Length.ToString(CultureInfo.CurrentCulture);
        if (_morePlayback.Length + _moreRecording.Length == 0)
        {
            _moreVisible = false;
        }
        UpdateMoreVisibility();

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
        PlaybackItems.ItemsSource = Array.Empty<EndpointRow>();
        RecordingItems.ItemsSource = Array.Empty<EndpointRow>();
        MorePlaybackItems.ItemsSource = Array.Empty<EndpointRow>();
        MoreRecordingItems.ItemsSource = Array.Empty<EndpointRow>();
        _morePlayback = [];
        _moreRecording = [];
        _moreVisible = false;
        PlaybackCountText.Text = "0 active";
        RecordingCountText.Text = "0 active";
        ActiveCountText.Text = "—";
        TotalCountText.Text = "endpoints unavailable";
        DefaultPlaybackText.Text = "Unavailable";
        DefaultRecordingText.Text = "Unavailable";
        StateBreakdownText.Text = "observation unavailable";
        NoPlaybackText.Visibility = Visibility.Visible;
        NoRecordingText.Visibility = Visibility.Visible;
        UpdateMoreVisibility();
        MixerProvenanceText.Text = "Windows Core Audio could not be reached. No values were synthesized.";
    }

    private void MoreEndpoints_Click(object sender, RoutedEventArgs e)
    {
        _moreVisible = !_moreVisible;
        UpdateMoreVisibility();
    }

    private void UpdateMoreVisibility()
    {
        int moreCount = _morePlayback.Length + _moreRecording.Length;
        bool canShow = moreCount > 0;
        MoreEndpointsButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
        MoreEndpointsPanel.Visibility = canShow && _moreVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreEndpointsButton.Content = _moreVisible
            ? "Show less"
            : $"Show {moreCount} more";
        MoreEndpointsButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            _moreVisible
                ? "Hide additional audio endpoints"
                : $"Show {moreCount} additional audio endpoints");
    }

    private static string DescribePrimaryCount(int shown, int active) =>
        shown == active ? $"{active} active" : $"{shown} of {active} active";

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
