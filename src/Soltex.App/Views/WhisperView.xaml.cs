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
    private static readonly int[] StandardRetentionDays = [1, 7, 30, 90];

    private readonly List<Button> _tabs;
    private readonly List<Button> _libraryTabs;
    private bool _updatingInputDevices;
    private bool _microphoneTestRunning;
    private bool _featureEnabled;
    private WhisperModelRequestedAction _modelAction = WhisperModelRequestedAction.Install;
    private bool _updatingPersonalization;
    private string[] _vocabularyTerms = [];
    private WhisperSnippet[] _snippets = [];
    private WhisperStyleProfile[] _customStyles = [];
    private WhisperAppProfile[] _applicationProfiles = [];
    private string _defaultStyleName = WhisperStyleProfile.Message.Name;
    private WhisperHistoryEntry[] _historyEntries = [];
    private bool _updatingPrivacy;
    private bool _autoSendEnabled;
    private bool _autoSendWarningAccepted;
    private bool _contextReadsAllowed;
    private bool _showTranscriptPreview;
    private WhisperOwnerAcceptanceAction _ownerAcceptanceAction;
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
            WhisperChecksTab,
            WhisperPersonalizeTab,
            WhisperLibraryTab,
            WhisperScratchpadTab,
            WhisperHistoryTab,
            WhisperPrivacyTab
        ];
        _libraryTabs =
        [
            WhisperSnippetsSectionTab,
            WhisperStylesSectionTab,
            WhisperApplicationsSectionTab
        ];
        WhisperShortcutList.ItemsSource = BuildShortcutRows();
        SetPersonalization(
            WhisperSettings.CreateDefault(),
            "Saved locally for this Windows account.");
        SetLibrary(
            WhisperSettings.CreateDefault(),
            "Saved locally for this Windows account.");
        SetHistory([], WhisperHistoryMode.SessionMemory, "Session memory only. Closing Soltex clears it.");
        SetPrivacy(WhisperSettings.CreateDefault(), "Safe defaults are active.");
        RefreshScratchpad();
        UpdateReadiness(CreateScaffoldInputs());
        SetOwnerAcceptance(
            new WhisperOwnerAcceptanceTracker().CreateSnapshot(),
            canDictate: false);
    }

    /// <summary>Raised when the user asks to see the listening surface.</summary>
    public event EventHandler? PreviewOverlayRequested;

    public event EventHandler? DeviceRefreshRequested;

    public event EventHandler<WhisperInputDeviceRequestedEventArgs>? InputDeviceRequested;

    public event EventHandler? MicrophoneTestRequested;

    public event EventHandler? MicrophoneTestStopRequested;

    public event EventHandler<WhisperFeatureEnabledRequestedEventArgs>?
        FeatureEnabledRequested;

    public event EventHandler? LocalProviderSelectRequested;

    public event EventHandler<WhisperModelActionRequestedEventArgs>?
        ModelActionRequested;

    public event EventHandler? ModelDeleteRequested;

    public event EventHandler<WhisperPersonalizationRequestedEventArgs>?
        PersonalizationRequested;

    public event EventHandler<WhisperLibraryRequestedEventArgs>? LibraryRequested;

    public event EventHandler<WhisperHistoryEntryRequestedEventArgs>?
        HistoryEntryDeleteRequested;

    public event EventHandler? HistoryClearRequested;

    public event EventHandler<WhisperPrivacyRequestedEventArgs>? PrivacyRequested;

    public event EventHandler<WhisperOwnerAcceptanceRequestedEventArgs>?
        OwnerAcceptanceRequested;

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
            ? "Validated global shortcuts are registered. Dictation starts only when every required Setup check is ready."
            : enabled
                ? "Whisper is on, but Windows shortcuts are not registered. Review the readiness error above."
                : "Whisper is off, so no global shortcut is registered.";
    }

    public void SetOwnerAcceptance(
        WhisperOwnerAcceptanceSnapshot snapshot,
        bool canDictate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        OwnerCheckRow[] rows = snapshot.Checks
            .Select(check => new OwnerCheckRow(check))
            .ToArray();
        WhisperOwnerCheckList.ItemsSource = rows;
        WhisperOwnerChecksCount.Text = $"{snapshot.PassedCount} of {rows.Length} passed";
        WhisperOwnerCheckResetButton.IsEnabled = rows.Any(row =>
            row.State != WhisperOwnerCheckState.Pending);
        WhisperScaffoldPill.Visibility = snapshot.IsComplete
            ? Visibility.Collapsed
            : Visibility.Visible;

        WhisperOwnerAcceptanceCheck? focus = snapshot.ActiveCheck is WhisperOwnerCheckKind active
            ? snapshot.Checks.Single(check => check.Kind == active)
            : snapshot.NextCheck is WhisperOwnerCheckKind next
                ? snapshot.Checks.Single(check => check.Kind == next)
                : null;
        if (snapshot.IsComplete)
        {
            WhisperOwnerCheckTitle.Text = "Owner checks complete";
            WhisperOwnerCheckDetail.Text =
                "All six checks passed in this session. No captured content was retained.";
            WhisperOwnerCheckActionButton.Content = "Complete";
            WhisperOwnerCheckActionButton.IsEnabled = false;
            _ownerAcceptanceAction = WhisperOwnerAcceptanceAction.None;
        }
        else if (snapshot.ActiveCheck == WhisperOwnerCheckKind.KeyboardAndScreenReader)
        {
            WhisperOwnerCheckTitle.Text = $"Running: {focus!.Title}";
            WhisperOwnerCheckDetail.Text = focus.Detail;
            WhisperOwnerCheckActionButton.Content = "Mark checked";
            WhisperOwnerCheckActionButton.IsEnabled = true;
            _ownerAcceptanceAction = WhisperOwnerAcceptanceAction.ConfirmAssistiveWalkthrough;
        }
        else if (snapshot.ActiveCheck == WhisperOwnerCheckKind.MicrophoneReconnect)
        {
            WhisperOwnerCheckTitle.Text = $"Running: {focus!.Title}";
            WhisperOwnerCheckDetail.Text = focus.Detail;
            WhisperOwnerCheckActionButton.Content = "Check devices";
            WhisperOwnerCheckActionButton.IsEnabled = true;
            _ownerAcceptanceAction = WhisperOwnerAcceptanceAction.RefreshMicrophones;
        }
        else if (snapshot.ActiveCheck is not null)
        {
            WhisperOwnerCheckTitle.Text = $"Running: {focus!.Title}";
            WhisperOwnerCheckDetail.Text = focus.Detail;
            WhisperOwnerCheckActionButton.Content = "Waiting";
            WhisperOwnerCheckActionButton.IsEnabled = false;
            _ownerAcceptanceAction = WhisperOwnerAcceptanceAction.None;
        }
        else
        {
            WhisperOwnerCheckTitle.Text = $"Next: {focus!.Title}";
            WhisperOwnerCheckDetail.Text = canDictate
                ? focus.Detail
                : "Finish every Setup prerequisite before starting owner checks.";
            WhisperOwnerCheckActionButton.Content = "Start next check";
            WhisperOwnerCheckActionButton.IsEnabled = canDictate;
            _ownerAcceptanceAction = WhisperOwnerAcceptanceAction.BeginNext;
        }

        AutomationProperties.SetName(
            WhisperOwnerCheckActionButton,
            _ownerAcceptanceAction switch
            {
                WhisperOwnerAcceptanceAction.BeginNext => "Start the next Whisper owner check",
                WhisperOwnerAcceptanceAction.RefreshMicrophones =>
                    "Refresh microphones for the reconnect check",
                WhisperOwnerAcceptanceAction.ConfirmAssistiveWalkthrough =>
                    "Confirm the keyboard and screen-reader walkthrough",
                _ => "Whisper owner check is waiting for an observed event"
            });
    }

    public void SetLocalModelStatus(
        WhisperSettings settings,
        WhisperModelStatus status,
        bool operationRunning,
        double progress,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        bool localSelected = string.Equals(
                settings.TranscriberId,
                WhisperLocalModelDefaults.ProviderId,
                StringComparison.Ordinal) &&
            string.Equals(
                settings.TranscriptionModelId,
                WhisperLocalModelDefaults.ModelId,
                StringComparison.Ordinal) &&
            string.Equals(
                settings.TranscriptionRuntimeId,
                WhisperLocalModelDefaults.RuntimeId,
                StringComparison.Ordinal);
        WhisperProviderSelectButton.Content = localSelected ? "Selected" : "Use local";
        WhisperProviderSelectButton.IsEnabled = !localSelected && !operationRunning;
        AutomationProperties.SetName(
            WhisperProviderSelectButton,
            localSelected
                ? "Local Whisper transcription provider selected"
                : "Select the local Whisper transcription provider");

        WhisperModelStatus.Text = detail;
        WhisperModelProgress.Visibility = operationRunning
            ? Visibility.Visible
            : Visibility.Collapsed;
        WhisperModelProgress.Value = Math.Clamp(progress, 0, 1) * 100;
        WhisperModelDeleteButton.Visibility = status.State is
                WhisperModelInstallState.Ready or WhisperModelInstallState.Invalid
            ? Visibility.Visible
            : Visibility.Collapsed;
        WhisperModelDeleteButton.IsEnabled = !operationRunning;

        Brush stateBrush;
        string stateLabel;
        if (operationRunning)
        {
            stateLabel = "INSTALLING";
            stateBrush = ThemeBrush("AccentBrush");
            _modelAction = WhisperModelRequestedAction.Cancel;
            WhisperModelActionButton.Content = "Cancel";
            AutomationProperties.SetName(
                WhisperModelActionButton,
                "Cancel the local Whisper model operation");
        }
        else
        {
            (stateLabel, stateBrush, _modelAction) = status.State switch
            {
                WhisperModelInstallState.Ready => (
                    "VERIFIED",
                    ThemeBrush("SignalBrush"),
                    WhisperModelRequestedAction.Repair),
                WhisperModelInstallState.Invalid or WhisperModelInstallState.Faulted => (
                    "REPAIR",
                    ThemeBrush("DangerBrush"),
                    WhisperModelRequestedAction.Repair),
                _ => (
                    "NOT INSTALLED",
                    ThemeBrush("WarningBrush"),
                    WhisperModelRequestedAction.Install)
            };
            WhisperModelActionButton.Content =
                _modelAction == WhisperModelRequestedAction.Install ? "Install" : "Repair";
            AutomationProperties.SetName(
                WhisperModelActionButton,
                _modelAction == WhisperModelRequestedAction.Install
                    ? "Install the local Whisper model"
                    : "Repair the local Whisper model");
        }

        WhisperModelStatePill.Text = stateLabel;
        WhisperModelStatePill.Foreground = stateBrush;
        WhisperModelActionButton.IsEnabled = true;
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
                .Concat(settings.CustomStyles)
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
            _defaultStyleName = settings.DefaultStyleName;
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

    public void SetLibrary(WhisperSettings settings, string detail)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        _snippets = settings.Snippets.ToArray();
        _customStyles = settings.CustomStyles.ToArray();
        _applicationProfiles = settings.ApplicationProfiles.ToArray();
        _defaultStyleName = settings.DefaultStyleName;

        WhisperSnippetList.ItemsSource = _snippets
            .Select(snippet => new SnippetRow(snippet))
            .ToArray();
        WhisperSnippetCount.Text =
            $"{_snippets.Length} of {WhisperSettings.MaximumStoredSnippetCount}";
        WhisperSnippetAddButton.IsEnabled =
            _snippets.Length < WhisperSettings.MaximumStoredSnippetCount;

        WhisperCustomStyleList.ItemsSource = _customStyles
            .Select(style => new CustomStyleRow(style))
            .ToArray();
        WhisperCustomStyleCount.Text =
            $"{_customStyles.Length} of {WhisperSettings.MaximumCustomStyleCount}";
        WhisperCustomStyleAddButton.IsEnabled =
            _customStyles.Length < WhisperSettings.MaximumCustomStyleCount;

        StyleBaseOption[] bases = WhisperStyleProfile.BuiltIn
            .Select(style => new StyleBaseOption(style, DescribeStyle(style)))
            .ToArray();
        WhisperCustomStyleBasePicker.ItemsSource = bases;
        WhisperCustomStyleBasePicker.SelectedItem = bases[0];

        StyleOption[] styles = WhisperStyleProfile.BuiltIn
            .Concat(_customStyles)
            .Select(style => new StyleOption(style.Name, style.Name))
            .ToArray();
        WhisperApplicationStylePicker.ItemsSource = styles;
        WhisperApplicationStylePicker.SelectedItem = styles.First();

        PermissionOption[] clipboardOptions =
        [
            new PermissionOption(true, "Allow copy fallback"),
            new PermissionOption(false, "Block copy fallback")
        ];
        WhisperApplicationClipboardPicker.ItemsSource = clipboardOptions;
        WhisperApplicationClipboardPicker.SelectedItem = clipboardOptions[0];

        PermissionOption[] optInOptions =
        [
            new PermissionOption(false, "Off"),
            new PermissionOption(true, "Eligible")
        ];
        WhisperApplicationContextPicker.ItemsSource = optInOptions;
        WhisperApplicationContextPicker.SelectedItem = optInOptions[0];
        WhisperApplicationAutoSendPicker.ItemsSource = optInOptions;
        WhisperApplicationAutoSendPicker.SelectedItem = optInOptions[0];
        WhisperApplicationTerminalSubmitPicker.ItemsSource = optInOptions;
        WhisperApplicationTerminalSubmitPicker.SelectedItem = optInOptions[0];

        WhisperApplicationProfileList.ItemsSource = _applicationProfiles
            .Select(profile => new ApplicationProfileRow(profile))
            .ToArray();
        WhisperApplicationProfileCount.Text =
            $"{_applicationProfiles.Length} of {WhisperSettings.MaximumApplicationProfileCount}";
        WhisperApplicationProfileAddButton.IsEnabled =
            _applicationProfiles.Length < WhisperSettings.MaximumApplicationProfileCount;

        WhisperSnippetCueInput.Clear();
        WhisperSnippetContentInput.Clear();
        WhisperCustomStyleNameInput.Clear();
        WhisperApplicationProcessInput.Clear();
        WhisperLibraryStatus.Text = detail;
    }

    public void SetLibraryError(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        WhisperLibraryStatus.Text = detail;
    }

    public void SetHistory(
        IReadOnlyList<WhisperHistoryEntry> entries,
        WhisperHistoryMode mode,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _historyEntries = entries.ToArray();
        WhisperHistoryList.ItemsSource = _historyEntries
            .Reverse()
            .Select(entry => new HistoryRow(entry))
            .ToArray();
        WhisperHistoryCount.Text = $"{_historyEntries.Length} " +
            (_historyEntries.Length == 1 ? "entry" : "entries");
        WhisperHistoryClearButton.IsEnabled = _historyEntries.Length > 0;
        WhisperHistoryEmptyState.Visibility = _historyEntries.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        WhisperHistoryStatus.Text = mode switch
        {
            WhisperHistoryMode.Off => "History is off. Completed transcripts are not retained.",
            WhisperHistoryMode.SessionMemory => detail,
            WhisperHistoryMode.EncryptedDisk => detail,
            _ => "History mode is unavailable."
        };
    }

    public void SetPrivacy(WhisperSettings settings, string detail)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _updatingPrivacy = true;
        try
        {
            _autoSendEnabled = settings.AutoSendEnabled;
            _autoSendWarningAccepted = settings.AutoSendWarningAccepted;
            _contextReadsAllowed = settings.ContextReadsAllowed;
            _showTranscriptPreview = settings.ShowTranscriptPreview;

            WhisperAutoSendDetail.Text = settings.AutoSendEnabled
                ? "On · every app, target, read-back, and submit check still applies."
                : "Off · Enter cannot be emitted.";
            WhisperAutoSendButton.Content = settings.AutoSendEnabled
                ? "Turn off"
                : settings.AutoSendWarningAccepted
                    ? "Turn on"
                    : "Review";
            AutomationProperties.SetName(
                WhisperAutoSendButton,
                settings.AutoSendEnabled
                    ? "Turn Whisper auto-send off"
                    : settings.AutoSendWarningAccepted
                        ? "Turn Whisper auto-send on"
                        : "Review the Whisper auto-send warning");

            WhisperContextReadsDetail.Text = settings.ContextReadsAllowed
                ? "On · only apps with their own visible permission are eligible."
                : "Off · text around the caret is not read.";
            WhisperContextReadsButton.Content = settings.ContextReadsAllowed
                ? "Turn off"
                : "Turn on";
            AutomationProperties.SetName(
                WhisperContextReadsButton,
                settings.ContextReadsAllowed
                    ? "Turn Whisper context reads off"
                    : "Turn Whisper context reads on");

            WhisperTranscriptPreviewDetail.Text = settings.ShowTranscriptPreview
                ? "On · dictated text may be visible above other applications."
                : "Off · the floating surface does not echo dictated text.";
            WhisperTranscriptPreviewButton.Content = settings.ShowTranscriptPreview
                ? "Turn off"
                : "Turn on";
            AutomationProperties.SetName(
                WhisperTranscriptPreviewButton,
                settings.ShowTranscriptPreview
                    ? "Turn Whisper transcript preview off"
                    : "Turn Whisper transcript preview on");

            HistoryModeOption[] historyModes =
            [
                new HistoryModeOption(WhisperHistoryMode.Off, "Off"),
                new HistoryModeOption(WhisperHistoryMode.SessionMemory, "This session only"),
                new HistoryModeOption(WhisperHistoryMode.EncryptedDisk, "Encrypted on this PC")
            ];
            WhisperHistoryModePicker.ItemsSource = historyModes;
            WhisperHistoryModePicker.SelectedItem = historyModes.Single(option =>
                option.Mode == settings.HistoryMode);
            HistoryRetentionOption[] retentionOptions = StandardRetentionDays
                .Append(settings.HistoryRetentionDays)
                .Distinct()
                .Order()
                .Select(days => new HistoryRetentionOption(
                    days,
                    days == 1 ? "1 day" : $"{days} days"))
                .ToArray();
            WhisperHistoryRetentionPicker.ItemsSource = retentionOptions;
            WhisperHistoryRetentionPicker.SelectedItem = retentionOptions.Single(option =>
                option.Days == settings.HistoryRetentionDays);
            WhisperHistoryRetentionPanel.Visibility =
                settings.HistoryMode == WhisperHistoryMode.EncryptedDisk
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            WhisperHistoryStorageDetail.Text = settings.HistoryMode switch
            {
                WhisperHistoryMode.Off => "Completed transcripts are not retained.",
                WhisperHistoryMode.SessionMemory => "Session memory is cleared when Soltex closes.",
                WhisperHistoryMode.EncryptedDisk =>
                    "Transcript text is DPAPI-protected inside authenticated Soltex state.",
                _ => "Choose a supported history mode."
            };

            ClipboardBehaviorOption[] clipboardBehaviors =
            [
                new ClipboardBehaviorOption(
                    WhisperClipboardBehavior.RestorePrevious,
                    "Restore previous contents when still owned"),
                new ClipboardBehaviorOption(
                    WhisperClipboardBehavior.LeaveTranscript,
                    "Leave transcript on clipboard")
            ];
            WhisperClipboardBehaviorPicker.ItemsSource = clipboardBehaviors;
            WhisperClipboardBehaviorPicker.SelectedItem = clipboardBehaviors.Single(option =>
                option.Behavior == settings.ClipboardBehavior);

            WhisperAutoSendWarningPanel.Visibility = Visibility.Collapsed;
            WhisperPrivacyStatus.Text = detail;
        }
        finally
        {
            _updatingPrivacy = false;
        }
    }

    public void SetPrivacyError(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        WhisperPrivacyStatus.Text = detail;
    }

    internal void ShowPersonalizationForEvidence() =>
        SelectTab(WhisperPersonalizeTab, WhisperPersonalizePanel);

    internal void ShowScratchpad() =>
        SelectTab(WhisperScratchpadTab, WhisperScratchpadPanel);

    internal void ShowLibraryForEvidence() =>
        SelectTab(WhisperLibraryTab, WhisperLibraryPanel);

    internal void ShowLibraryStylesForEvidence()
    {
        ShowLibraryForEvidence();
        SelectLibrarySection(WhisperStylesSectionTab, WhisperStylesSection);
    }

    internal void ShowLibraryApplicationsForEvidence()
    {
        ShowLibraryForEvidence();
        SelectLibrarySection(WhisperApplicationsSectionTab, WhisperApplicationsSection);
    }

    internal void ShowHistoryForEvidence() =>
        SelectTab(WhisperHistoryTab, WhisperHistoryPanel);

    internal void ShowPrivacyForEvidence() =>
        SelectTab(WhisperPrivacyTab, WhisperPrivacyPanel);

    internal void ShowChecksForEvidence() =>
        SelectTab(WhisperChecksTab, WhisperChecksPanel);

    internal void ShowEncryptedPrivacyForEvidence()
    {
        WhisperSettingsDocument document = WhisperSettings.CreateDefault().ToDocument();
        document.HistoryMode = WhisperHistoryMode.EncryptedDisk.ToString();
        document.HistoryRetentionDays = 30;
        SetPrivacy(
            WhisperSettingsMigrator.Load(document).Settings,
            "Encrypted retention is configured for this Windows account.");
        SelectTab(WhisperPrivacyTab, WhisperPrivacyPanel);
    }

    internal void ShowPrivacyWarningForEvidence()
    {
        ShowPrivacyForEvidence();
        WhisperAutoSendWarningPanel.Visibility = Visibility.Visible;
        WhisperPrivacyStatus.Text = "Review the auto-send boundary before enabling it.";
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

    private void FeatureToggle_Click(object sender, RoutedEventArgs e) =>
        FeatureEnabledRequested?.Invoke(
            this,
            new WhisperFeatureEnabledRequestedEventArgs(!_featureEnabled));

    private void ProviderSelect_Click(object sender, RoutedEventArgs e) =>
        LocalProviderSelectRequested?.Invoke(this, EventArgs.Empty);

    private void ModelAction_Click(object sender, RoutedEventArgs e) =>
        ModelActionRequested?.Invoke(
            this,
            new WhisperModelActionRequestedEventArgs(_modelAction));

    private void ModelDelete_Click(object sender, RoutedEventArgs e) =>
        ModelDeleteRequested?.Invoke(this, EventArgs.Empty);

    private void WhisperSetupTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperSetupTab, WhisperSetupPanel);

    private void WhisperShortcutsTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperShortcutsTab, WhisperShortcutsPanel);

    private void WhisperChecksTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperChecksTab, WhisperChecksPanel);

    private void WhisperPersonalizeTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperPersonalizeTab, WhisperPersonalizePanel);

    private void WhisperLibraryTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperLibraryTab, WhisperLibraryPanel);

    private void WhisperScratchpadTab_Click(object sender, RoutedEventArgs e) =>
        ShowScratchpad();

    private void WhisperHistoryTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(WhisperHistoryTab, WhisperHistoryPanel);

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
        WhisperChecksPanel.Visibility = Visibility.Collapsed;
        WhisperPersonalizePanel.Visibility = Visibility.Collapsed;
        WhisperLibraryPanel.Visibility = Visibility.Collapsed;
        WhisperScratchpadPanel.Visibility = Visibility.Collapsed;
        WhisperHistoryPanel.Visibility = Visibility.Collapsed;
        WhisperPrivacyPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void OwnerCheckAction_Click(object sender, RoutedEventArgs e)
    {
        if (_ownerAcceptanceAction != WhisperOwnerAcceptanceAction.None)
        {
            OwnerAcceptanceRequested?.Invoke(
                this,
                new WhisperOwnerAcceptanceRequestedEventArgs(_ownerAcceptanceAction));
        }
    }

    private void OwnerCheckReset_Click(object sender, RoutedEventArgs e) =>
        OwnerAcceptanceRequested?.Invoke(
            this,
            new WhisperOwnerAcceptanceRequestedEventArgs(WhisperOwnerAcceptanceAction.Reset));

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

    private void WhisperSnippetsSectionTab_Click(object sender, RoutedEventArgs e) =>
        SelectLibrarySection(WhisperSnippetsSectionTab, WhisperSnippetsSection);

    private void WhisperStylesSectionTab_Click(object sender, RoutedEventArgs e) =>
        SelectLibrarySection(WhisperStylesSectionTab, WhisperStylesSection);

    private void WhisperApplicationsSectionTab_Click(object sender, RoutedEventArgs e) =>
        SelectLibrarySection(WhisperApplicationsSectionTab, WhisperApplicationsSection);

    private void SelectLibrarySection(Button tab, UIElement panel)
    {
        foreach (Button candidate in _libraryTabs)
        {
            candidate.Tag = ReferenceEquals(candidate, tab) ? "Selected" : null;
        }

        WhisperSnippetsSection.Visibility = Visibility.Collapsed;
        WhisperStylesSection.Visibility = Visibility.Collapsed;
        WhisperApplicationsSection.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WhisperSnippet snippet = new(
                WhisperSnippetCueInput.Text,
                WhisperSnippetContentInput.Text);
            if (snippet.Content.Length > WhisperSettings.MaximumStoredSnippetContentCharacters)
            {
                throw new ArgumentException("Snippet content exceeded the stored size limit.");
            }

            if (_snippets.Any(existing => string.Equals(
                    existing.Cue,
                    snippet.Cue,
                    StringComparison.OrdinalIgnoreCase)))
            {
                SetLibraryError("That spoken cue already exists.");
                return;
            }

            RequestLibrary(_snippets.Append(snippet), _customStyles, _applicationProfiles);
        }
        catch (ArgumentException)
        {
            SetLibraryError("Use a unique printable cue and no more than 4,000 characters of text.");
        }
    }

    private void RemoveSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SnippetRow row })
        {
            RequestLibrary(
                _snippets.Where(snippet => !string.Equals(
                    snippet.Cue,
                    row.Cue,
                    StringComparison.OrdinalIgnoreCase)),
                _customStyles,
                _applicationProfiles);
        }
    }

    private void AddCustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperCustomStyleBasePicker.SelectedItem is not StyleBaseOption selected)
        {
            SetLibraryError("Choose one visible formatting rule set.");
            return;
        }

        try
        {
            WhisperStyleProfile template = selected.Profile;
            WhisperStyleProfile style = new(
                WhisperCustomStyleNameInput.Text,
                template.Kind,
                template.ProseCleanup,
                template.SpokenPunctuation,
                template.PreserveLiteralTokens,
                template.CapitalizeSentences);
            if (WhisperStyleProfile.BuiltIn.Concat(_customStyles).Any(existing =>
                    string.Equals(existing.Name, style.Name, StringComparison.OrdinalIgnoreCase)))
            {
                SetLibraryError("That style name already exists.");
                return;
            }

            RequestLibrary(_snippets, _customStyles.Append(style), _applicationProfiles);
        }
        catch (ArgumentException)
        {
            SetLibraryError("Use a unique printable style name of at most 64 characters.");
        }
    }

    private void RemoveCustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CustomStyleRow row })
        {
            return;
        }

        if (string.Equals(_defaultStyleName, row.Name, StringComparison.OrdinalIgnoreCase) ||
            _applicationProfiles.Any(profile => string.Equals(
                profile.StyleName,
                row.Name,
                StringComparison.OrdinalIgnoreCase)))
        {
            SetLibraryError("Change the default and app rules that use this style before removing it.");
            return;
        }

        RequestLibrary(
            _snippets,
            _customStyles.Where(style => !string.Equals(
                style.Name,
                row.Name,
                StringComparison.OrdinalIgnoreCase)),
            _applicationProfiles);
    }

    private void AddApplicationProfile_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperApplicationStylePicker.SelectedItem is not StyleOption style ||
            WhisperApplicationClipboardPicker.SelectedItem is not PermissionOption clipboard ||
            WhisperApplicationContextPicker.SelectedItem is not PermissionOption context ||
            WhisperApplicationAutoSendPicker.SelectedItem is not PermissionOption autoSend ||
            WhisperApplicationTerminalSubmitPicker.SelectedItem is not PermissionOption terminal)
        {
            SetLibraryError("Choose each application permission before saving.");
            return;
        }

        if (terminal.IsAllowed && !autoSend.IsAllowed)
        {
            SetLibraryError("Terminal submit eligibility also requires app auto-send eligibility.");
            return;
        }

        try
        {
            WhisperAppProfile profile = new(
                WhisperApplicationProcessInput.Text,
                autoSend.IsAllowed,
                terminal.IsAllowed,
                clipboard.IsAllowed,
                context.IsAllowed,
                style.Name);
            IEnumerable<WhisperAppProfile> remaining = _applicationProfiles.Where(existing =>
                !existing.MatchesProcess(profile.ProcessName));
            RequestLibrary(_snippets, _customStyles, remaining.Append(profile));
        }
        catch (ArgumentException)
        {
            SetLibraryError("Use a process name only, without a path or reserved characters.");
        }
    }

    private void RemoveApplicationProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ApplicationProfileRow row })
        {
            RequestLibrary(
                _snippets,
                _customStyles,
                _applicationProfiles.Where(profile => !profile.MatchesProcess(row.ProcessName)));
        }
    }

    private void RequestLibrary(
        IEnumerable<WhisperSnippet> snippets,
        IEnumerable<WhisperStyleProfile> customStyles,
        IEnumerable<WhisperAppProfile> applicationProfiles)
    {
        LibraryRequested?.Invoke(
            this,
            new WhisperLibraryRequestedEventArgs(
                snippets.ToArray(),
                customStyles.ToArray(),
                applicationProfiles.ToArray()));
    }

    private void DeleteHistoryEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HistoryRow row })
        {
            RequestHistoryEntryDelete(row.Entry);
        }
    }

    internal void RequestHistoryEntryDelete(WhisperHistoryEntry entry) =>
        HistoryEntryDeleteRequested?.Invoke(
            this,
            new WhisperHistoryEntryRequestedEventArgs(entry));

    private void ClearHistory_Click(object sender, RoutedEventArgs e) =>
        HistoryClearRequested?.Invoke(this, EventArgs.Empty);

    private void AutoSend_Click(object sender, RoutedEventArgs e)
    {
        if (_autoSendEnabled)
        {
            RequestPrivacy(autoSendEnabled: false);
        }
        else if (_autoSendWarningAccepted)
        {
            RequestPrivacy(autoSendEnabled: true);
        }
        else
        {
            WhisperAutoSendWarningPanel.Visibility = Visibility.Visible;
            WhisperPrivacyStatus.Text = "Review the auto-send boundary before enabling it.";
        }
    }

    private void ConfirmAutoSendWarning_Click(object sender, RoutedEventArgs e) =>
        RequestPrivacy(autoSendEnabled: true, warningAccepted: true);

    private void CancelAutoSendWarning_Click(object sender, RoutedEventArgs e)
    {
        WhisperAutoSendWarningPanel.Visibility = Visibility.Collapsed;
        WhisperPrivacyStatus.Text = "Auto-send remains off.";
    }

    private void ContextReads_Click(object sender, RoutedEventArgs e) =>
        RequestPrivacy(contextReadsAllowed: !_contextReadsAllowed);

    private void TranscriptPreview_Click(object sender, RoutedEventArgs e) =>
        RequestPrivacy(showTranscriptPreview: !_showTranscriptPreview);

    private void PrivacyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingPrivacy)
        {
            RequestPrivacy();
        }
    }

    private void RequestPrivacy(
        bool? autoSendEnabled = null,
        bool? warningAccepted = null,
        bool? contextReadsAllowed = null,
        bool? showTranscriptPreview = null)
    {
        if (WhisperHistoryModePicker.SelectedItem is not HistoryModeOption history ||
            WhisperHistoryRetentionPicker.SelectedItem is not HistoryRetentionOption retention ||
            WhisperClipboardBehaviorPicker.SelectedItem is not ClipboardBehaviorOption clipboard)
        {
            SetPrivacyError("Choose a supported history and clipboard mode.");
            return;
        }

        PrivacyRequested?.Invoke(
            this,
            new WhisperPrivacyRequestedEventArgs(
                autoSendEnabled ?? _autoSendEnabled,
                warningAccepted ?? _autoSendWarningAccepted,
                contextReadsAllowed ?? _contextReadsAllowed,
                history.Mode,
                retention.Days,
                clipboard.Behavior,
                showTranscriptPreview ?? _showTranscriptPreview));
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

    internal sealed class OwnerCheckRow
    {
        internal OwnerCheckRow(WhisperOwnerAcceptanceCheck check)
        {
            Kind = check.Kind;
            State = check.State;
            Title = check.Title;
            Detail = check.Detail;
            StateLabel = check.State switch
            {
                WhisperOwnerCheckState.Pending => "NOT RUN",
                WhisperOwnerCheckState.Waiting => "WAITING",
                WhisperOwnerCheckState.Observed => "OBSERVED",
                WhisperOwnerCheckState.Passed => "PASSED",
                _ => "RETRY"
            };
            StateBrush = check.State switch
            {
                WhisperOwnerCheckState.Passed => ThemeBrush("SignalBrush"),
                WhisperOwnerCheckState.Waiting or WhisperOwnerCheckState.Observed =>
                    ThemeBrush("AccentBrush"),
                WhisperOwnerCheckState.NeedsAttention => ThemeBrush("WarningBrush"),
                _ => ThemeBrush("MutedBrush")
            };
        }

        public WhisperOwnerCheckKind Kind { get; }

        public WhisperOwnerCheckState State { get; }

        public string Title { get; }

        public string Detail { get; }

        public string StateLabel { get; }

        public Brush StateBrush { get; }
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

    internal sealed record StyleBaseOption(WhisperStyleProfile Profile, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed record PermissionOption(bool IsAllowed, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed class SnippetRow
    {
        internal SnippetRow(WhisperSnippet snippet)
        {
            Cue = snippet.Cue;
            string collapsed = string.Join(
                ' ',
                snippet.Content.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Preview = collapsed.Length <= 140 ? collapsed : $"{collapsed[..140]}…";
        }

        public string Cue { get; }

        public string Preview { get; }

        public string RemoveAutomationName => $"Remove snippet {Cue}";
    }

    internal sealed class CustomStyleRow
    {
        internal CustomStyleRow(WhisperStyleProfile style)
        {
            Name = style.Name;
            Summary = DescribeStyle(style);
        }

        public string Name { get; }

        public string Summary { get; }

        public string RemoveAutomationName => $"Remove custom style {Name}";
    }

    internal sealed class ApplicationProfileRow
    {
        internal ApplicationProfileRow(WhisperAppProfile profile)
        {
            ProcessName = profile.ProcessName;
            List<string> permissions = [$"Style: {profile.StyleName}"];
            permissions.Add(profile.ClipboardFallbackAllowed ? "copy fallback" : "copy blocked");
            if (profile.ContextFormattingAllowed)
            {
                permissions.Add("context eligible");
            }

            if (profile.AutoSendAllowed)
            {
                permissions.Add("auto-send eligible");
            }

            if (profile.TerminalAutoSendAllowed)
            {
                permissions.Add("terminal submit eligible");
            }

            Summary = string.Join(" · ", permissions);
        }

        public string ProcessName { get; }

        public string Summary { get; }

        public string RemoveAutomationName => $"Remove application rule for {ProcessName}";
    }

    internal sealed class HistoryRow
    {
        internal HistoryRow(WhisperHistoryEntry entry)
        {
            Entry = entry;
            ProcessName = entry.ProcessName;
            Meta = $"{entry.CreatedAtUtc.ToLocalTime():t} · {DescribeDelivery(entry.DeliveryKind)}";
            string collapsed = string.Join(
                ' ',
                entry.Text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Preview = collapsed.Length <= 180 ? collapsed : $"{collapsed[..180]}…";
        }

        internal WhisperHistoryEntry Entry { get; }

        public string ProcessName { get; }

        public string Meta { get; }

        public string Preview { get; }

        public string DeleteAutomationName =>
            $"Delete Whisper history entry for {ProcessName} at {Entry.CreatedAtUtc.ToLocalTime():t}";
    }

    internal sealed record HistoryModeOption(WhisperHistoryMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed record HistoryRetentionOption(int Days, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    internal sealed record ClipboardBehaviorOption(
        WhisperClipboardBehavior Behavior,
        string DisplayName)
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

    private static string DescribeStyle(WhisperStyleProfile style)
    {
        List<string> rules = [];
        rules.Add(style.ProseCleanup ? "clean prose" : "keep literal phrasing");
        rules.Add(style.SpokenPunctuation ? "spoken punctuation" : "literal punctuation words");
        if (style.PreserveLiteralTokens)
        {
            rules.Add("preserve identifiers");
        }

        rules.Add(style.CapitalizeSentences ? "sentence case" : "preserve case");
        return $"{style.Kind} · {string.Join(" · ", rules)}";
    }

    private static string DescribeDelivery(WhisperDeliveryKind kind) => kind switch
    {
        WhisperDeliveryKind.CopyText => "Copied",
        WhisperDeliveryKind.InsertText => "Inserted",
        WhisperDeliveryKind.InsertAndSubmit => "Inserted and submitted",
        WhisperDeliveryKind.SubmitOnly => "Submitted",
        _ => "Completed"
    };
}

public enum WhisperOwnerAcceptanceAction
{
    None,
    BeginNext,
    RefreshMicrophones,
    ConfirmAssistiveWalkthrough,
    Reset
}

public sealed class WhisperOwnerAcceptanceRequestedEventArgs(
    WhisperOwnerAcceptanceAction action) : EventArgs
{
    public WhisperOwnerAcceptanceAction Action { get; } = action;
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

public enum WhisperModelRequestedAction
{
    Install,
    Repair,
    Cancel
}

public sealed class WhisperModelActionRequestedEventArgs(
    WhisperModelRequestedAction action) : EventArgs
{
    public WhisperModelRequestedAction Action { get; } = action;
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

public sealed class WhisperLibraryRequestedEventArgs : EventArgs
{
    public WhisperLibraryRequestedEventArgs(
        IReadOnlyList<WhisperSnippet> snippets,
        IReadOnlyList<WhisperStyleProfile> customStyles,
        IReadOnlyList<WhisperAppProfile> applicationProfiles)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(customStyles);
        ArgumentNullException.ThrowIfNull(applicationProfiles);
        Snippets = Array.AsReadOnly(snippets.ToArray());
        CustomStyles = Array.AsReadOnly(customStyles.ToArray());
        ApplicationProfiles = Array.AsReadOnly(applicationProfiles.ToArray());
    }

    public IReadOnlyList<WhisperSnippet> Snippets { get; }

    public IReadOnlyList<WhisperStyleProfile> CustomStyles { get; }

    public IReadOnlyList<WhisperAppProfile> ApplicationProfiles { get; }
}

public sealed class WhisperHistoryEntryRequestedEventArgs(WhisperHistoryEntry entry) : EventArgs
{
    public WhisperHistoryEntry Entry { get; } =
        entry ?? throw new ArgumentNullException(nameof(entry));
}

public sealed class WhisperPrivacyRequestedEventArgs : EventArgs
{
    public WhisperPrivacyRequestedEventArgs(
        bool autoSendEnabled,
        bool autoSendWarningAccepted,
        bool contextReadsAllowed,
        WhisperHistoryMode historyMode,
        int historyRetentionDays,
        WhisperClipboardBehavior clipboardBehavior,
        bool showTranscriptPreview)
    {
        AutoSendEnabled = autoSendEnabled;
        AutoSendWarningAccepted = autoSendWarningAccepted;
        ContextReadsAllowed = contextReadsAllowed;
        HistoryMode = historyMode;
        HistoryRetentionDays = historyRetentionDays;
        ClipboardBehavior = clipboardBehavior;
        ShowTranscriptPreview = showTranscriptPreview;
    }

    public bool AutoSendEnabled { get; }

    public bool AutoSendWarningAccepted { get; }

    public bool ContextReadsAllowed { get; }

    public WhisperHistoryMode HistoryMode { get; }

    public int HistoryRetentionDays { get; }

    public WhisperClipboardBehavior ClipboardBehavior { get; }

    public bool ShowTranscriptPreview { get; }
}
