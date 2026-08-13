using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soltex.Audio;

namespace Soltex.App.Views;

internal sealed class AudioSessionChangeRequestedEventArgs : EventArgs
{
    internal AudioSessionChangeRequestedEventArgs(
        AudioSession session,
        AudioSessionMutationKind kind,
        double? requestedVolumePercent,
        bool? requestedMute)
    {
        Session = session;
        Kind = kind;
        RequestedVolumePercent = requestedVolumePercent;
        RequestedMute = requestedMute;
    }

    internal AudioSession Session { get; }

    internal AudioSessionMutationKind Kind { get; }

    internal double? RequestedVolumePercent { get; }

    internal bool? RequestedMute { get; }
}

internal sealed class AudioEndpointPreferenceRequestedEventArgs(
    AudioEndpoint endpoint) : EventArgs
{
    internal AudioEndpoint Endpoint { get; } = endpoint;
}

public partial class MixerView : UserControl
{
    private const int PrimaryEndpointLimit = 6;
    private const int PrimarySessionLimit = 5;
    private EndpointRow[] _morePlayback = [];
    private EndpointRow[] _moreRecording = [];
    private SessionRow[] _moreSessions = [];
    private bool _moreVisible;
    private bool _deviceDetailsVisible;
    private bool _moreSessionsVisible;
    private bool _sessionControlsBusy;
    private bool _mixSnapshotBusy;
    private bool _hasControllableSessions;
    private AudioMixSnapshot? _mixSnapshot;
    private AudioEndpointSnapshot? _lastEndpointSnapshot;
    private AudioObservationState? _lastSessionState;
    private string _preferredPlaybackEndpointKey = string.Empty;
    private string _preferredRecordingEndpointKey = string.Empty;

    public MixerView()
    {
        InitializeComponent();
    }

    internal event EventHandler? RefreshRequested;

    internal event EventHandler<AudioSessionChangeRequestedEventArgs>? SessionChangeRequested;

    internal event EventHandler<AudioEndpointPreferenceRequestedEventArgs>? EndpointPreferenceRequested;

    internal event EventHandler? ClearEndpointPreferencesRequested;

    internal event EventHandler? OpenSoundSettingsRequested;

    internal event EventHandler? CaptureMixSnapshotRequested;

    internal event EventHandler? ApplyMixSnapshotRequested;

    internal event EventHandler? ClearMixSnapshotRequested;

    internal AudioMixSnapshot? MixSnapshot => _mixSnapshot;

    internal void UpdateMixSnapshot(AudioMixLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _mixSnapshotBusy = false;
        _mixSnapshot = result.Snapshot;
        MixSnapshotStateText.Text = result.RecoveredFromInvalid
            ? "RECOVERED"
            : result.Snapshot is null ? "NOT SAVED" : "SAVED";
        MixSnapshotStateText.Foreground = (Brush)FindResource(
            result.RecoveredFromInvalid
                ? "WarningBrush"
                : result.Snapshot is null ? "QuietTextBrush" : "SignalBrush");
        MixSnapshotDetailText.Text = result.Detail;
        UpdateMixSnapshotButtons();
    }

    internal void ShowMixSnapshotPending(string detail)
    {
        SetMixSnapshotBusy(true);
        MixSnapshotStateText.Text = "CHECKING";
        MixSnapshotStateText.Foreground = (Brush)FindResource("WarningBrush");
        MixSnapshotDetailText.Text = detail;
    }

    internal void ShowMixSnapshotResult(
        string state,
        string brushKey,
        string detail)
    {
        SetMixSnapshotBusy(false);
        MixSnapshotStateText.Text = state;
        MixSnapshotStateText.Foreground = (Brush)FindResource(brushKey);
        MixSnapshotDetailText.Text = detail;
    }

    internal void UpdateEndpointPreferences(string playbackKey, string recordingKey)
    {
        _preferredPlaybackEndpointKey = SoltexPreferences.NormalizeEndpointPreferenceKey(playbackKey);
        _preferredRecordingEndpointKey = SoltexPreferences.NormalizeEndpointPreferenceKey(recordingKey);
        if (_lastEndpointSnapshot is not null)
        {
            UpdateSnapshot(_lastEndpointSnapshot);
        }
        else
        {
            UpdatePreferenceStatus(null);
        }
    }

    public void UpdateSnapshot(
        AudioEndpointSnapshot endpointSnapshot,
        AudioSessionSnapshot sessionSnapshot)
    {
        _lastSessionState = sessionSnapshot.State;
        UpdateSnapshot(endpointSnapshot);
        UpdateSessions(sessionSnapshot);
        RenderState(MergeState(endpointSnapshot.State, sessionSnapshot.State));
        MixerProvenanceText.Text =
            $"Windows Core Audio · device read {endpointSnapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            $"session read {sessionSnapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            $"{endpointSnapshot.InaccessibleEndpointCount} device records unavailable · " +
            $"{sessionSnapshot.InaccessibleSessionCount} session records unavailable · " +
            $"{sessionSnapshot.OmittedSessionCount} session records omitted";
    }

    public void UpdateSnapshot(AudioEndpointSnapshot snapshot)
    {
        _lastEndpointSnapshot = snapshot;
        RenderState(snapshot.State);

        EndpointPalette palette = new(
            Active: (Brush)FindResource("SignalBrush"),
            Attention: (Brush)FindResource("WarningBrush"),
            Quiet: (Brush)FindResource("QuietTextBrush"),
            Text: (Brush)FindResource("TextBrush"),
            Track: (Brush)FindResource("TrackBrush"));

        EndpointRow[] playback = BuildRows(
            snapshot.Render,
            palette,
            _preferredPlaybackEndpointKey);
        EndpointRow[] recording = BuildRows(
            snapshot.Capture,
            palette,
            _preferredRecordingEndpointKey);
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
        UpdatePreferenceStatus(snapshot);

        MixerProvenanceText.Text =
            $"{snapshot.Provenance} · captured {snapshot.CapturedAtUtc.ToLocalTime():T} · " +
            $"provider {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · " +
            $"{snapshot.InaccessibleEndpointCount} inaccessible · " +
            string.Join(" · ", snapshot.Limitations);
        if (_lastSessionState is AudioObservationState sessionState)
        {
            RenderState(MergeState(snapshot.State, sessionState));
        }
    }

    public void ShowUnavailable()
    {
        _lastSessionState = AudioObservationState.Unavailable;
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
        UpdatePreferenceStatus(null);
        NoPlaybackText.Visibility = Visibility.Visible;
        NoRecordingText.Visibility = Visibility.Visible;
        _deviceDetailsVisible = false;
        SessionItems.ItemsSource = Array.Empty<SessionRow>();
        MoreSessionItems.ItemsSource = Array.Empty<SessionRow>();
        _moreSessions = [];
        _moreSessionsVisible = false;
        NoSessionsText.Visibility = Visibility.Visible;
        SessionSummaryText.Text = "Active playback-session observation is unavailable.";
        UpdateMoreSessionsVisibility();
        SetSessionActionState("UNAVAILABLE", "DangerBrush",
            "Windows Core Audio sessions could not be reached. No controls were enabled.");
        UpdateDeviceDetailsVisibility();
        UpdateMoreVisibility();
        MixerProvenanceText.Text = "Windows Core Audio could not be reached. No values were synthesized.";
    }

    internal void ShowSessionMutationPending(AudioSession session, AudioSessionMutationKind kind)
    {
        SetSessionControlsBusy(true);
        SetSessionActionState(
            "CHECKING",
            "WarningBrush",
            $"Revalidating {session.Name} before the {DescribeMutation(kind)} request.");
    }

    internal void ShowSessionMutationResult(AudioSessionMutationResult result)
    {
        SetSessionControlsBusy(false);
        string brush = result.Status switch
        {
            AudioSessionMutationStatus.Applied => "SignalBrush",
            AudioSessionMutationStatus.Rejected or AudioSessionMutationStatus.TargetChanged => "WarningBrush",
            _ => "DangerBrush"
        };
        SetSessionActionState(
            result.Status == AudioSessionMutationStatus.Applied ? "VERIFIED" : "CHECK",
            brush,
            result.Message);
    }

    internal void ShowSessionRefreshFailure()
    {
        SetSessionControlsBusy(false);
        SetSessionActionState(
            "UNAVAILABLE",
            "DangerBrush",
            "Active playback sessions could not be refreshed. Existing controls were disabled.");
        SessionItems.IsEnabled = false;
        MoreSessionItems.IsEnabled = false;
    }

    private void UpdateSessions(AudioSessionSnapshot snapshot)
    {
        SessionRow[] rows = snapshot.Sessions
            .Select(session => new SessionRow(session))
            .ToArray();
        SessionRow[] primary = rows.Take(PrimarySessionLimit).ToArray();
        _moreSessions = rows.Skip(primary.Length).ToArray();
        SessionItems.ItemsSource = primary;
        MoreSessionItems.ItemsSource = _moreSessions;
        NoSessionsText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionSummaryText.Text = DescribeSessions(snapshot);
        _hasControllableSessions = rows.Any(row => row.CanControl);
        if (_moreSessions.Length == 0)
        {
            _moreSessionsVisible = false;
        }

        UpdateMoreSessionsVisibility();
        SetSessionControlsBusy(false);
        UpdateMixSnapshotButtons();
    }

    private void RefreshAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionControlsBusy)
        {
            return;
        }

        SetSessionControlsBusy(true);
        SetSessionActionState("REFRESHING", "WarningBrush", "Reading current endpoint and app-session state.");
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoreSessions_Click(object sender, RoutedEventArgs e)
    {
        _moreSessionsVisible = !_moreSessionsVisible;
        UpdateMoreSessionsVisibility();
    }

    private void SessionVolume_Commit(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider { Tag: SessionRow row } slider)
        {
            RequestSessionChange(row, AudioSessionMutationKind.Volume, slider.Value, null);
        }
    }

    private void SessionVolume_CommitKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or
            Key.Home or Key.End or Key.PageUp or Key.PageDown))
        {
            return;
        }

        if (sender is Slider { Tag: SessionRow row } slider)
        {
            RequestSessionChange(row, AudioSessionMutationKind.Volume, slider.Value, null);
        }
    }

    private void SessionMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionRow row })
        {
            RequestSessionChange(row, AudioSessionMutationKind.Mute, null, !row.Session.IsMuted);
        }
    }

    private void CaptureMixSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_mixSnapshotBusy || !_hasControllableSessions)
        {
            return;
        }

        CaptureMixSnapshotRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMixSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_mixSnapshotBusy || _mixSnapshot is null)
        {
            return;
        }

        ApplyMixSnapshotRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearMixSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_mixSnapshotBusy || _mixSnapshot is null)
        {
            return;
        }

        ClearMixSnapshotRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestSessionChange(
        SessionRow row,
        AudioSessionMutationKind kind,
        double? volumePercent,
        bool? muted)
    {
        if (_sessionControlsBusy || !row.CanControl)
        {
            return;
        }

        ShowSessionMutationPending(row.Session, kind);
        SessionChangeRequested?.Invoke(
            this,
            new AudioSessionChangeRequestedEventArgs(row.Session, kind, volumePercent, muted));
    }

    private void UpdateMoreSessionsVisibility()
    {
        bool canShow = _moreSessions.Length > 0;
        MoreSessionsButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
        MoreSessionItems.Visibility = canShow && _moreSessionsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreSessionsButton.Content = _moreSessionsVisible
            ? "Show less"
            : $"Show {_moreSessions.Length} more";
        MoreSessionsButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            _moreSessionsVisible
                ? "Hide additional active audio sessions"
                : $"Show {_moreSessions.Length} additional active audio sessions");
    }

    private void SetSessionControlsBusy(bool busy)
    {
        _sessionControlsBusy = busy;
        SessionItems.IsEnabled = !busy;
        MoreSessionItems.IsEnabled = !busy;
        MoreSessionsButton.IsEnabled = !busy;
        RefreshAudioButton.IsEnabled = !busy;
        UpdateMixSnapshotButtons();
    }

    private void SetMixSnapshotBusy(bool busy)
    {
        _mixSnapshotBusy = busy;
        SetSessionControlsBusy(busy);
        UpdateMixSnapshotButtons();
    }

    private void UpdateMixSnapshotButtons()
    {
        bool ready = !_mixSnapshotBusy && !_sessionControlsBusy;
        CaptureMixSnapshotButton.IsEnabled = ready && _hasControllableSessions;
        ApplyMixSnapshotButton.IsEnabled = ready && _mixSnapshot is not null;
        ClearMixSnapshotButton.IsEnabled = ready && _mixSnapshot is not null;
        CaptureMixSnapshotButton.Style = (Style)FindResource(
            _mixSnapshot is null ? "ActionButton" : "SecondaryButton");
        ApplyMixSnapshotButton.Style = (Style)FindResource(
            _mixSnapshot is null ? "SecondaryButton" : "ActionButton");
    }

    private void SetSessionActionState(string state, string brushKey, string detail)
    {
        SessionActionStateText.Text = state;
        SessionActionStateText.Foreground = (Brush)FindResource(brushKey);
        SessionActionDetailText.Text = detail;
    }

    private static string DescribeSessions(AudioSessionSnapshot snapshot)
    {
        int controllable = snapshot.Sessions.Count(session => session.CanControl);
        string active = snapshot.Sessions.Count == 1
            ? "1 active playback session"
            : $"{snapshot.Sessions.Count} active playback sessions";
        string omitted = snapshot.OmittedSessionCount > 0
            ? $" · {snapshot.OmittedSessionCount} omitted by bounds"
            : string.Empty;
        return $"{active} · {controllable} controllable{omitted}";
    }

    private static string DescribeMutation(AudioSessionMutationKind kind) =>
        kind == AudioSessionMutationKind.Volume ? "volume" : "mute";

    private static AudioObservationState MergeState(
        AudioObservationState endpoints,
        AudioObservationState sessions)
    {
        if (endpoints == AudioObservationState.Unavailable &&
            sessions == AudioObservationState.Unavailable)
        {
            return AudioObservationState.Unavailable;
        }

        return endpoints == AudioObservationState.Current &&
            sessions == AudioObservationState.Current
            ? AudioObservationState.Current
            : AudioObservationState.Partial;
    }

    private void MoreEndpoints_Click(object sender, RoutedEventArgs e)
    {
        _moreVisible = !_moreVisible;
        UpdateMoreVisibility();
    }

    private void DeviceDetails_Click(object sender, RoutedEventArgs e)
    {
        _deviceDetailsVisible = !_deviceDetailsVisible;
        UpdateDeviceDetailsVisibility();
    }

    private void EndpointPreference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EndpointRow row })
        {
            RequestEndpointPreference(row.Endpoint);
        }
    }

    internal void RequestEndpointPreference(AudioEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string currentKey = endpoint.Direction == AudioEndpointDirection.Render
            ? _preferredPlaybackEndpointKey
            : _preferredRecordingEndpointKey;
        string normalizedKey = SoltexPreferences.NormalizeEndpointPreferenceKey(endpoint.PreferenceKey);
        if (endpoint.State != AudioEndpointState.Active ||
            normalizedKey.Length == 0 ||
            !string.Equals(normalizedKey, endpoint.PreferenceKey, StringComparison.Ordinal) ||
            string.Equals(normalizedKey, currentKey, StringComparison.Ordinal))
        {
            return;
        }

        EndpointPreferenceStateText.Text = "SAVING";
        EndpointPreferenceStateText.Foreground = (Brush)FindResource("WarningBrush");
        EndpointPreferenceDetailText.Text = $"Remembering {endpoint.Name} as a fallback reminder.";
        EndpointPreferenceRequested?.Invoke(
            this,
            new AudioEndpointPreferenceRequestedEventArgs(endpoint));
    }

    private void ClearEndpointPreferences_Click(object sender, RoutedEventArgs e) =>
        ClearEndpointPreferencesRequested?.Invoke(this, EventArgs.Empty);

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSoundSettingsRequested?.Invoke(this, EventArgs.Empty);

    internal void ShowEndpointPreferenceResult(bool saved, string detail)
    {
        EndpointPreferenceStateText.Text = saved ? "REMEMBERED" : "CHECK";
        EndpointPreferenceStateText.Foreground =
            (Brush)FindResource(saved ? "SignalBrush" : "DangerBrush");
        EndpointPreferenceDetailText.Text = detail;
    }

    internal void ShowSoundSettingsResult(WindowsSoundSettingsLaunchResult result)
    {
        EndpointPreferenceStateText.Text = result.Started ? "WINDOWS" : "CHECK";
        EndpointPreferenceStateText.Foreground =
            (Brush)FindResource(result.Started ? "SignalBrush" : "DangerBrush");
        EndpointPreferenceDetailText.Text = result.Message;
    }

    private void UpdatePreferenceStatus(AudioEndpointSnapshot? snapshot)
    {
        bool hasPlayback = !string.IsNullOrEmpty(_preferredPlaybackEndpointKey);
        bool hasRecording = !string.IsNullOrEmpty(_preferredRecordingEndpointKey);
        ClearEndpointPreferencesButton.IsEnabled = hasPlayback || hasRecording;
        if (!hasPlayback && !hasRecording)
        {
            EndpointPreferenceStateText.Text = "OPTIONAL";
            EndpointPreferenceStateText.Foreground = (Brush)FindResource("QuietTextBrush");
            EndpointPreferenceDetailText.Text =
                "No fallback reminders saved. Windows remains the owner of system default selection.";
            return;
        }

        bool playbackReady = hasPlayback && snapshot?.Render.Any(endpoint =>
            endpoint.State == AudioEndpointState.Active &&
            string.Equals(
                endpoint.PreferenceKey,
                _preferredPlaybackEndpointKey,
                StringComparison.Ordinal)) == true;
        bool recordingReady = hasRecording && snapshot?.Capture.Any(endpoint =>
            endpoint.State == AudioEndpointState.Active &&
            string.Equals(
                endpoint.PreferenceKey,
                _preferredRecordingEndpointKey,
                StringComparison.Ordinal)) == true;
        int saved = (hasPlayback ? 1 : 0) + (hasRecording ? 1 : 0);
        int ready = (playbackReady ? 1 : 0) + (recordingReady ? 1 : 0);
        EndpointPreferenceStateText.Text = ready == saved ? "READY" : "CHECK";
        EndpointPreferenceStateText.Foreground =
            (Brush)FindResource(ready == saved ? "SignalBrush" : "WarningBrush");
        EndpointPreferenceDetailText.Text = ready == saved
            ? $"{saved} saved fallback {(saved == 1 ? "device is" : "devices are")} currently active."
            : $"{ready} of {saved} saved fallback devices are currently active.";
    }

    private void UpdateDeviceDetailsVisibility()
    {
        MixerOverviewPanel.Visibility = _deviceDetailsVisible
            ? Visibility.Collapsed
            : Visibility.Visible;
        EndpointDetailsPanel.Visibility = _deviceDetailsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeviceDetailsButton.Content = _deviceDetailsVisible ? "Mixer" : "Audio devices";
        DeviceDetailsButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            _deviceDetailsVisible ? "Show app audio mixer" : "Show audio device details");
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
    private static EndpointRow[] BuildRows(
        IEnumerable<AudioEndpoint> endpoints,
        EndpointPalette palette,
        string preferredKey) =>
        endpoints
            .OrderBy(endpoint => StateRank(endpoint.State))
            .ThenByDescending(endpoint => endpoint.IsDefault)
            .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(endpoint => new EndpointRow(endpoint, palette, preferredKey))
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

    private sealed class SessionRow(AudioSession session)
    {
        internal AudioSession Session { get; } = session;

        public string Name => Session.Name;

        public string EndpointName => Session.EndpointName;

        public double VolumePercent => Session.VolumePercent;

        public string VolumeText => $"{Session.VolumePercent:F0}%";

        public bool CanControl => Session.CanControl;

        public string MuteActionText => Session.IsMuted ? "Unmute" : "Mute";

        public string MuteAccessibleName =>
            $"{MuteActionText} {Session.Name}";

        public string VolumeAccessibleName =>
            $"{Session.Name} volume {Session.VolumePercent:F0} percent";

        public string Tooltip =>
            $"{Session.Name} · {Session.EndpointName} · {Session.ControlAvailability}";
    }

    private sealed class EndpointRow
    {
        private readonly AudioEndpoint _endpoint;
        private readonly EndpointPalette _palette;
        private readonly bool _isPreferred;

        internal EndpointRow(AudioEndpoint endpoint, EndpointPalette palette, string preferredKey)
        {
            _endpoint = endpoint;
            _palette = palette;
            _isPreferred = !string.IsNullOrEmpty(preferredKey) &&
                string.Equals(endpoint.PreferenceKey, preferredKey, StringComparison.Ordinal);
        }

        internal AudioEndpoint Endpoint => _endpoint;

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

        public Visibility PreferredVisibility => _isPreferred ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MuteVisibility => _endpoint.IsMuted == true ? Visibility.Visible : Visibility.Collapsed;

        public bool CanSelectAsPreference =>
            IsActive &&
            SoltexPreferences.NormalizeEndpointPreferenceKey(_endpoint.PreferenceKey).Length > 0 &&
            !_isPreferred;

        public string PreferenceActionText => _isPreferred ? "Preferred" : "Prefer";

        public string PreferenceAccessibleName => _isPreferred
            ? $"{_endpoint.Name} is the saved fallback reminder"
            : $"Remember {_endpoint.Name} as the fallback reminder";

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
