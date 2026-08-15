using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soltex.Whisper;
using Soltex.Whisper.Windows;

namespace Soltex.App.Views;

/// <summary>
/// The Whisper workspace. It leads with readiness rather than settings: the common
/// failure is a shortcut that appears to do nothing, and the answer to that has to be
/// one sentence on the page rather than a toggle the user has to find.
/// </summary>
public partial class WhisperView : UserControl
{
    private readonly List<Button> _tabs;
    private bool _updatingInputDevices;
    private bool _microphoneTestRunning;

    public WhisperView()
    {
        InitializeComponent();

        _tabs = [WhisperSetupTab, WhisperShortcutsTab, WhisperPrivacyTab];
        WhisperShortcutList.ItemsSource = BuildShortcutRows();
        UpdateReadiness(CreateScaffoldInputs());
    }

    /// <summary>Raised when the user asks to see the listening surface.</summary>
    public event EventHandler? PreviewOverlayRequested;

    public event EventHandler? DeviceRefreshRequested;

    public event EventHandler<WhisperInputDeviceRequestedEventArgs>? InputDeviceRequested;

    public event EventHandler? MicrophoneTestRequested;

    public event EventHandler? MicrophoneTestStopRequested;

    /// <summary>
    /// Renders a readiness report. The host supplies the observed facts; this view
    /// only presents them, so the page cannot claim a capability the runtime lacks.
    /// </summary>
    public void UpdateReadiness(WhisperReadinessInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        WhisperReadinessReport report = WhisperReadinessEvaluator.Evaluate(inputs);
        ReadinessRow[] rows = report.Checks.Select(check => new ReadinessRow(check)).ToArray();

        WhisperReadinessList.ItemsSource = rows;
        WhisperReadinessCount.Text =
            $"{rows.Count(row => row.IsReady)} of {rows.Length} ready";

        WhisperReadinessTitle.Text = report.CanDictate
            ? "Whisper is ready to dictate"
            : "Whisper is not ready";
        WhisperReadinessDetail.Text = report.Summary;
        WhisperReadinessAction.Text = report.PrimaryBlocker?.NextAction?.ToUpperInvariant() ?? string.Empty;

        Brush stateBrush = report.CanDictate
            ? ThemeBrush("SignalBrush")
            : ThemeBrush("WarningBrush");
        WhisperReadinessDot.Fill = stateBrush;
        WhisperReadinessAction.Foreground = stateBrush;
    }

    public void UpdateCaptureDevices(
        WhisperCaptureDeviceSnapshot snapshot,
        string? configuredDeviceId,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        _updatingInputDevices = true;
        try
        {
            List<InputDeviceOption> options =
            [
                new InputDeviceOption(null, "Choose a microphone")
            ];
            options.AddRange(snapshot.Devices.Select(device => new InputDeviceOption(
                device.Id,
                device.IsDefault ? $"{device.Name} · Windows default" : device.Name)));
            WhisperInputDevicePicker.ItemsSource = options;
            WhisperInputDevicePicker.SelectedItem = options.FirstOrDefault(option =>
                string.Equals(option.Id, configuredDeviceId, StringComparison.Ordinal)) ?? options[0];
            WhisperCaptureStatus.Text = detail;
            WhisperMicrophoneTestButton.IsEnabled =
                !_microphoneTestRunning &&
                WhisperInputDevicePicker.SelectedItem is InputDeviceOption { Id: not null };
        }
        finally
        {
            _updatingInputDevices = false;
        }
    }

    public void SetMicrophoneTestState(bool running, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _microphoneTestRunning = running;
        WhisperCaptureStatus.Text = detail;
        WhisperInputDevicePicker.IsEnabled = !running;
        WhisperRefreshDevicesButton.IsEnabled = !running;
        WhisperMicrophoneTestButton.IsEnabled = running ||
            WhisperInputDevicePicker.SelectedItem is InputDeviceOption { Id: not null };
        WhisperMicrophoneTestButton.Content = running ? "Stop test" : "Test microphone";
        AutomationProperties.SetName(
            WhisperMicrophoneTestButton,
            running ? "Stop the microphone test" : "Start a five second microphone test");
    }

    private void PreviewOverlay_Click(object sender, RoutedEventArgs e) =>
        PreviewOverlayRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) =>
        DeviceRefreshRequested?.Invoke(this, EventArgs.Empty);

    private void InputDevicePicker_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingInputDevices ||
            WhisperInputDevicePicker.SelectedItem is not InputDeviceOption option)
        {
            return;
        }

        InputDeviceRequested?.Invoke(
            this,
            new WhisperInputDeviceRequestedEventArgs(option.Id));
    }

    private void MicrophoneTest_Click(object sender, RoutedEventArgs e)
    {
        if (_microphoneTestRunning)
        {
            MicrophoneTestStopRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            MicrophoneTestRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WhisperSetupTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperSetupTab, WhisperSetupPanel);

    private void WhisperShortcutsTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperShortcutsTab, WhisperShortcutsPanel);

    private void WhisperPrivacyTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperPrivacyTab, WhisperPrivacyPanel);

    private void SelectTab(Button tab, UIElement panel)
    {
        foreach (Button candidate in _tabs)
        {
            candidate.Tag = ReferenceEquals(candidate, tab) ? "Selected" : null;
        }

        WhisperSetupPanel.Visibility = Visibility.Collapsed;
        WhisperShortcutsPanel.Visibility = Visibility.Collapsed;
        WhisperPrivacyPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The facts this build can honestly report. Capture, providers, and global hooks
    /// do not exist yet, so the checklist says so instead of showing a ready state.
    /// </summary>
    private static WhisperReadinessInputs CreateScaffoldInputs() => new(
        FeatureEnabled: false,
        MicrophoneSelected: false,
        MicrophonePermissionGranted: true,
        ShortcutsRegistered: false,
        ShortcutRegistrationError: null,
        TranscriberConfigured: false,
        TranscriberCredentialAvailable: false,
        TargetInspectionAvailable: false,
        AutoSendEnabled: false,
        AutoSendWarningAccepted: false,
        EnabledAutoSendProfileCount: 0);

    private static ShortcutRow[] BuildShortcutRows()
    {
        WhisperShortcutSet defaults = WhisperShortcutSet.CreateDefault();
        return defaults.Bindings
            .Select(binding => new ShortcutRow(
                DescribeAction(binding.Action),
                binding.Keys.ToArray(),
                DescribeBehavior(binding.Action)))
            .ToArray();
    }

    private static string DescribeAction(WhisperShortcutAction action) => action switch
    {
        WhisperShortcutAction.PushToTalk => "Push to talk",
        WhisperShortcutAction.HandsFree => "Hands-free",
        WhisperShortcutAction.CommandMode => "Command",
        WhisperShortcutAction.PasteLastTranscript => "Paste last",
        WhisperShortcutAction.CopyLastTranscript => "Copy last",
        WhisperShortcutAction.Cancel => "Cancel",
        WhisperShortcutAction.OpenScratchpad => "Scratchpad",
        WhisperShortcutAction.SubmitLastTranscript => "Submit last",
        _ => action.ToString()
    };

    private static string DescribeBehavior(WhisperShortcutAction action) => action switch
    {
        WhisperShortcutAction.PushToTalk => "Hold to dictate, release to insert",
        WhisperShortcutAction.HandsFree => "Toggle; warns at 19 minutes, stops at 20",
        WhisperShortcutAction.CommandMode => "Hold to transform the selection",
        WhisperShortcutAction.PasteLastTranscript => "Insert the last completed transcript",
        WhisperShortcutAction.CopyLastTranscript => "Copy the last completed transcript",
        WhisperShortcutAction.Cancel => "Discard the current session",
        WhisperShortcutAction.OpenScratchpad => "Open the Scratchpad",
        WhisperShortcutAction.SubmitLastTranscript => "Submit, subject to every auto-send check",
        _ => string.Empty
    };

    private static Brush ThemeBrush(string key) =>
        (Brush)Application.Current.Resources[key];

    /// <summary>One readiness row, projected for the template.</summary>
    internal sealed class ReadinessRow
    {
        internal ReadinessRow(WhisperReadinessCheck check)
        {
            Title = check.Title;
            Detail = check.NextAction is null
                ? check.Detail
                : $"{check.Detail} — {check.NextAction}.";
            IsReady = check.State == WhisperReadinessState.Ready;
            StateLabel = check.State switch
            {
                WhisperReadinessState.Ready => "READY",
                WhisperReadinessState.NeedsSetup => "SET UP",
                _ => "BLOCKED"
            };
            StateBrush = check.State switch
            {
                WhisperReadinessState.Ready => ThemeBrush("SignalBrush"),
                WhisperReadinessState.NeedsSetup => ThemeBrush("WarningBrush"),
                _ => ThemeBrush("DangerBrush")
            };
        }

        public string Title { get; }

        public string Detail { get; }

        public string StateLabel { get; }

        public Brush StateBrush { get; }

        internal bool IsReady { get; }
    }

    /// <summary>One shortcut row: action, keycaps, and what it does.</summary>
    internal sealed class ShortcutRow
    {
        internal ShortcutRow(string action, IReadOnlyList<string> keys, string description)
        {
            Action = action;
            Keys = keys;
            Description = description;
        }

        public string Action { get; }

        public IReadOnlyList<string> Keys { get; }

        public string Description { get; }
    }

    private sealed record InputDeviceOption(string? Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

public sealed class WhisperInputDeviceRequestedEventArgs : EventArgs
{
    public WhisperInputDeviceRequestedEventArgs(string? deviceId)
    {
        DeviceId = deviceId;
    }

    public string? DeviceId { get; }
}
