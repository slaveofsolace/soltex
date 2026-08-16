using System.Globalization;
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
    private bool _featureEnabled;
    private bool _updatingPersonalization;
    private string[] _vocabularyTerms = [];
    private readonly WhisperScratchpad _scratchpad = new();
    private bool _updatingScratchpad;
    private int? _pendingScratchpadClose;

    public WhisperView()
    {
        InitializeComponent();

        _tabs =
        [
            WhisperSetupTab,
            WhisperShortcutsTab,
            WhisperPersonalizeTab,
            WhisperScratchpadTab,
            WhisperPrivacyTab
        ];
        WhisperShortcutList.ItemsSource = BuildShortcutRows();
        SetPersonalization(
            WhisperSettings.CreateDefault(),
            "Saved locally for this Windows account.");
        RefreshScratchpad();
        UpdateReadiness(CreateScaffoldInputs());
    }

    /// <summary>Raised when the user asks to see the listening surface.</summary>
    public event EventHandler? PreviewOverlayRequested;

    public event EventHandler? DeviceRefreshRequested;

    public event EventHandler<WhisperInputDeviceRequestedEventArgs>? InputDeviceRequested;

    public event EventHandler? MicrophoneTestRequested;

    public event EventHandler? MicrophoneTestStopRequested;

    public event EventHandler<WhisperFeatureEnabledRequestedEventArgs>?
        FeatureEnabledRequested;

    public event EventHandler<WhisperPersonalizationRequestedEventArgs>?
        PersonalizationRequested;

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

    public void SetFeatureState(
        bool enabled,
        bool updating,
        bool shortcutsRegistered,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _featureEnabled = enabled;
        WhisperFeatureStatus.Text = detail;
        WhisperFeatureToggleButton.IsEnabled = !updating;
        WhisperFeatureToggleButton.Content = updating
            ? "Applying"
            : enabled
                ? "Turn off"
                : "Turn on";
        AutomationProperties.SetName(
            WhisperFeatureToggleButton,
            updating
                ? "Applying Whisper runtime setting"
                : enabled
                    ? "Turn Whisper off"
                    : "Turn Whisper on");
        WhisperShortcutStatus.Text = shortcutsRegistered
            ? "Validated global shortcuts are registered. Until a provider is configured, a shortcut opens a clear setup message instead of recording."
            : enabled
                ? "Whisper is on, but Windows shortcuts are not registered. Review the readiness error above."
                : "Whisper is off, so no global shortcut is registered.";
    }

    public void SetPersonalization(WhisperSettings settings, string detail)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        _updatingPersonalization = true;
        try
        {
            List<LanguageOption> languages = BuildLanguageOptions().ToList();
            string? languageTag = settings.Language.LanguageTag;
            if (languageTag is not null && !languages.Any(option =>
                string.Equals(option.Tag, languageTag, StringComparison.OrdinalIgnoreCase)))
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(
                    languageTag,
                    predefinedOnly: true);
                languages.Add(new LanguageOption(
                    culture.Name,
                    $"{culture.EnglishName} · {culture.Name}"));
            }

            WhisperLanguagePicker.ItemsSource = languages;
            WhisperLanguagePicker.SelectedItem = languages.First(option =>
                string.Equals(option.Tag, languageTag, StringComparison.OrdinalIgnoreCase));

            List<StyleOption> styles = WhisperStyleProfile.BuiltIn
                .Select(style => new StyleOption(style.Name, style.Name))
                .ToList();
            if (!styles.Any(option => string.Equals(
                    option.Name,
                    settings.DefaultStyleName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                styles.Add(new StyleOption(
                    settings.DefaultStyleName,
                    $"{settings.DefaultStyleName} · custom"));
            }

            WhisperStylePicker.ItemsSource = styles;
            WhisperStylePicker.SelectedItem = styles.First(option => string.Equals(
                option.Name,
                settings.DefaultStyleName,
                StringComparison.OrdinalIgnoreCase));

            _vocabularyTerms = settings.Vocabulary.Terms.ToArray();
            WhisperVocabularyList.ItemsSource = _vocabularyTerms
                .Select(term => new VocabularyRow(term))
                .ToArray();
            WhisperVocabularyCount.Text =
                $"{_vocabularyTerms.Length} of {WhisperVocabulary.MaximumEntries} terms";
            WhisperPersonalizationStatus.Text = detail;
            WhisperVocabularyInput.Clear();
        }
        finally
        {
            _updatingPersonalization = false;
        }
    }

    public void SetPersonalizationError(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        WhisperPersonalizationStatus.Text = detail;
    }

    internal void ShowPersonalizationForEvidence() =>
        SelectTab(WhisperPersonalizeTab, WhisperPersonalizePanel);

    internal void ShowScratchpad() =>
        SelectTab(WhisperScratchpadTab, WhisperScratchpadPanel);

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

    private void FeatureToggle_Click(object sender, RoutedEventArgs e) =>
        FeatureEnabledRequested?.Invoke(
            this,
            new WhisperFeatureEnabledRequestedEventArgs(!_featureEnabled));

    private void WhisperSetupTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperSetupTab, WhisperSetupPanel);

    private void WhisperShortcutsTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperShortcutsTab, WhisperShortcutsPanel);

    private void WhisperPersonalizeTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperPersonalizeTab, WhisperPersonalizePanel);

    private void WhisperScratchpadTab_Click(object sender, RoutedEventArgs e) =>
        ShowScratchpad();

    private void WhisperPrivacyTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperPrivacyTab, WhisperPrivacyPanel);

    private void SelectTab(Button tab, UIElement panel)
    {
        _pendingScratchpadClose = null;
        foreach (Button candidate in _tabs)
        {
            candidate.Tag = ReferenceEquals(candidate, tab) ? "Selected" : null;
        }

        WhisperSetupPanel.Visibility = Visibility.Collapsed;
        WhisperShortcutsPanel.Visibility = Visibility.Collapsed;
        WhisperPersonalizePanel.Visibility = Visibility.Collapsed;
        WhisperScratchpadPanel.Visibility = Visibility.Collapsed;
        WhisperPrivacyPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void PersonalizationPicker_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_updatingPersonalization)
        {
            RequestPersonalization(_vocabularyTerms);
        }
    }

    private void AddVocabulary_Click(object sender, RoutedEventArgs e)
    {
        string term = WhisperVocabularyInput.Text.Trim();
        if (term.Length == 0)
        {
            SetPersonalizationError("Enter one name or term before adding it.");
            return;
        }

        RequestPersonalization(_vocabularyTerms.Concat([term]).ToArray());
    }

    private void RemoveVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: VocabularyRow row })
        {
            return;
        }

        RequestPersonalization(_vocabularyTerms
            .Where(term => !string.Equals(term, row.Term, StringComparison.OrdinalIgnoreCase))
            .ToArray());
    }

    private void RequestPersonalization(IReadOnlyList<string> vocabularyTerms)
    {
        if (WhisperLanguagePicker.SelectedItem is not LanguageOption language ||
            WhisperStylePicker.SelectedItem is not StyleOption style)
        {
            SetPersonalizationError("Choose a supported language and style.");
            return;
        }

        PersonalizationRequested?.Invoke(
            this,
            new WhisperPersonalizationRequestedEventArgs(
                language.Tag,
                style.Name,
                vocabularyTerms));
    }

    private void ScratchpadText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingScratchpad)
        {
            return;
        }

        _pendingScratchpadClose = null;
        _scratchpad.Active.Replace(WhisperScratchpadTextBox.Text);
        RefreshScratchpad();
    }

    private void ScratchpadAddTab_Click(object sender, RoutedEventArgs e)
    {
        _pendingScratchpadClose = null;
        _scratchpad.AddTab();
        RefreshScratchpad("New session-only note created.");
        WhisperScratchpadTextBox.Focus();
    }

    private void ActivateScratchpadTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ScratchpadTabRow row })
        {
            return;
        }

        _pendingScratchpadClose = null;
        _scratchpad.Activate(row.Index);
        RefreshScratchpad();
        WhisperScratchpadTextBox.Focus();
    }

    private void CloseScratchpadTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ScratchpadTabRow row })
        {
            return;
        }

        WhisperScratchpadTab tab = _scratchpad.Tabs[row.Index];
        if (tab.Content.Length > 0 && _pendingScratchpadClose != row.Index)
        {
            _pendingScratchpadClose = row.Index;
            RefreshScratchpad($"Press close again to remove {tab.Title} from this session.");
            return;
        }

        _pendingScratchpadClose = null;
        bool clearingLastTab = _scratchpad.Tabs.Count == 1;
        _scratchpad.CloseTab(row.Index);
        RefreshScratchpad(clearingLastTab
            ? "Last note cleared. Undo is available."
            : "Scratchpad note closed.");
    }

    private void ScratchpadUndo_Click(object sender, RoutedEventArgs e)
    {
        _pendingScratchpadClose = null;
        _ = _scratchpad.Active.Undo();
        RefreshScratchpad("Last Scratchpad change undone.");
    }

    private void ScratchpadRedo_Click(object sender, RoutedEventArgs e)
    {
        _pendingScratchpadClose = null;
        _ = _scratchpad.Active.Redo();
        RefreshScratchpad("Scratchpad change restored.");
    }

    private void ScratchpadClear_Click(object sender, RoutedEventArgs e)
    {
        _pendingScratchpadClose = null;
        _scratchpad.Active.Clear();
        RefreshScratchpad("Active note cleared. Undo is available.");
    }

    private void RefreshScratchpad(string? detail = null)
    {
        _updatingScratchpad = true;
        try
        {
            WhisperScratchpadTabs.ItemsSource = _scratchpad.Tabs
                .Select((tab, index) => new ScratchpadTabRow(
                    index,
                    tab.Title,
                    index == _scratchpad.ActiveIndex))
                .ToArray();
            WhisperScratchpadTextBox.Text = _scratchpad.Active.Content;
            WhisperScratchpadTextBox.CaretIndex = WhisperScratchpadTextBox.Text.Length;
            WhisperScratchpadUndoButton.IsEnabled = _scratchpad.Active.CanUndo;
            WhisperScratchpadRedoButton.IsEnabled = _scratchpad.Active.CanRedo;
            WhisperScratchpadClearButton.IsEnabled = _scratchpad.Active.Content.Length > 0;
            WhisperScratchpadAddTabButton.IsEnabled = _scratchpad.CanAddTab;
            WhisperScratchpadStatus.Text = detail ??
                $"Session memory only · {_scratchpad.Active.Content.Length:N0} characters";
        }
        finally
        {
            _updatingScratchpad = false;
        }
    }

    /// <summary>
    /// The facts this view can honestly report before its runtime host supplies
    /// validated settings and observed Windows capability.
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

    private static LanguageOption[] BuildLanguageOptions()
    {
        List<LanguageOption> options = [new(null, "Auto-detect")];
        foreach (string languageName in WhisperTranslationLanguages.SupportedNames)
        {
            if (!WhisperTranslationLanguages.TryResolve(languageName, out string? tag) ||
                tag is null)
            {
                continue;
            }

            CultureInfo culture = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            options.Add(new LanguageOption(
                culture.Name,
                $"{culture.EnglishName} · {culture.Name}"));
        }

        return options
            .Take(1)
            .Concat(options.Skip(1).OrderBy(option => option.DisplayName, StringComparer.Ordinal))
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

    internal sealed record VocabularyRow(string Term)
    {
        public string RemoveAutomationName => $"Remove {Term} from personal vocabulary";
    }

    internal sealed record LanguageOption(string? Tag, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed record StyleOption(string Name, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed record ScratchpadTabRow(int Index, string Title, bool IsActive)
    {
        public string? SelectionTag => IsActive ? "Selected" : null;

        public string OpenAutomationName => $"Open Scratchpad note {Title}";

        public string CloseAutomationName => $"Close Scratchpad note {Title}";
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

public sealed class WhisperFeatureEnabledRequestedEventArgs(bool enabled) : EventArgs
{
    public bool Enabled { get; } = enabled;
}

public sealed class WhisperPersonalizationRequestedEventArgs : EventArgs
{
    public WhisperPersonalizationRequestedEventArgs(
        string? languageTag,
        string styleName,
        IReadOnlyList<string> vocabularyTerms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleName);
        ArgumentNullException.ThrowIfNull(vocabularyTerms);
        LanguageTag = languageTag;
        StyleName = styleName;
        VocabularyTerms = Array.AsReadOnly(vocabularyTerms.ToArray());
    }

    public string? LanguageTag { get; }

    public string StyleName { get; }

    public IReadOnlyList<string> VocabularyTerms { get; }
}
