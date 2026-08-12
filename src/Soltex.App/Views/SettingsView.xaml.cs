using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soltex.App.Views;

internal sealed class PreferencesChangedEventArgs(SoltexPreferences preferences) : EventArgs
{
    internal SoltexPreferences Preferences { get; } = preferences;
}

public partial class SettingsView : UserControl
{
    private SoltexPreferences _preferences = SoltexPreferences.Default;
    private bool _notificationAreaAvailable;

    public SettingsView()
    {
        InitializeComponent();
        RenderPreferences();
    }

    internal event EventHandler<PreferencesChangedEventArgs>? PreferencesChanged;

    internal void UpdateNotificationAreaAvailability(bool available)
    {
        _notificationAreaAvailable = available;
        NotificationAreaButton.IsEnabled = available;
        NotificationAreaButton.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.HelpTextProperty,
            available
                ? "Available for this Windows session"
                : "Unavailable for this Windows session; closing exits Soltex");
        RenderPreferences();
    }

    internal void UpdatePreferences(
        SoltexPreferences preferences,
        bool recoveredFromInvalid,
        string detail)
    {
        _preferences = preferences.Normalize();
        RenderPreferences();
        SettingsSaveDetailText.Text = detail;
        SetState(
            recoveredFromInvalid ? "CHECK" : "LOCAL",
            recoveredFromInvalid ? "WarningBrush" : "SignalBrush");
    }

    internal void ShowSaved()
    {
        SettingsSaveDetailText.Text = "Saved for this Windows account.";
        SetState("SAVED", "SignalBrush");
    }

    internal void ShowSaveFailure()
    {
        SettingsSaveDetailText.Text =
            "The preference could not be saved. The current session still uses this choice.";
        SetState("NOT SAVED", "DangerBrush");
    }

    private void LiveCadence_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with { TelemetryCadence = TelemetryCadence.Live });

    private void BalancedCadence_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with { TelemetryCadence = TelemetryCadence.Balanced });

    private void QuietCadence_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with { TelemetryCadence = TelemetryCadence.Quiet });

    private void PerformanceDetails_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            OpenPerformanceDetails = !_preferences.OpenPerformanceDetails
        });

    private void RestoreWorkspace_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            RestoreLastWorkspace = !_preferences.RestoreLastWorkspace
        });

    private void ExitOnClose_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            CloseBehavior = CloseBehavior.Exit
        });

    private void NotificationArea_Click(object sender, RoutedEventArgs e)
    {
        if (_notificationAreaAvailable)
        {
            Commit(_preferences with
            {
                CloseBehavior = CloseBehavior.NotificationArea
            });
        }
    }

    private void SessionActivity_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            ActivityRetention = ActivityRetention.SessionOnly
        });

    private void SevenDayActivity_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            ActivityRetention = ActivityRetention.SevenDays
        });

    private void ThirtyDayActivity_Click(object sender, RoutedEventArgs e) =>
        Commit(_preferences with
        {
            ActivityRetention = ActivityRetention.ThirtyDays
        });

    private void GeneralSettingsTab_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsPanel(GeneralSettingsPanel, GeneralSettingsTab);

    private void WindowSettingsTab_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsPanel(WindowSettingsPanel, WindowSettingsTab);

    private void PrivacySettingsTab_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsPanel(PrivacySettingsPanel, PrivacySettingsTab);

    private void AboutSettingsTab_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsPanel(AboutSettingsPanel, AboutSettingsTab);

    private void ResetDefaults_Click(object sender, RoutedEventArgs e) =>
        Commit(SoltexPreferences.Default);

    private void Commit(SoltexPreferences preferences)
    {
        _preferences = preferences.Normalize();
        RenderPreferences();
        SetState("SAVING", "WarningBrush");
        PreferencesChanged?.Invoke(
            this,
            new PreferencesChangedEventArgs(_preferences));
    }

    private void RenderPreferences()
    {
        SetSelected(
            LiveCadenceButton,
            _preferences.TelemetryCadence == TelemetryCadence.Live);
        SetSelected(
            BalancedCadenceButton,
            _preferences.TelemetryCadence == TelemetryCadence.Balanced);
        SetSelected(
            QuietCadenceButton,
            _preferences.TelemetryCadence == TelemetryCadence.Quiet);
        SetToggle(PerformanceDetailsButton, _preferences.OpenPerformanceDetails);
        SetToggle(RestoreWorkspaceButton, _preferences.RestoreLastWorkspace);
        SetSelected(
            ExitOnCloseButton,
            _preferences.CloseBehavior == CloseBehavior.Exit ||
            !_notificationAreaAvailable);
        SetSelected(
            NotificationAreaButton,
            _preferences.CloseBehavior == CloseBehavior.NotificationArea &&
            _notificationAreaAvailable);
        SetSelected(
            SessionActivityButton,
            _preferences.ActivityRetention == ActivityRetention.SessionOnly);
        SetSelected(
            SevenDayActivityButton,
            _preferences.ActivityRetention == ActivityRetention.SevenDays);
        SetSelected(
            ThirtyDayActivityButton,
            _preferences.ActivityRetention == ActivityRetention.ThirtyDays);
        bool background =
            _preferences.CloseBehavior == CloseBehavior.NotificationArea &&
            _notificationAreaAvailable;
        NotificationAreaDetailText.Text = !_notificationAreaAvailable
            ? "The Windows notification area is unavailable in this session. Closing exits Soltex."
            : background
                ? "Close hides the window. Use the Soltex icon to reopen or explicitly exit."
                : "Exit ends the Soltex process. Notification-area mode is opt-in.";
        BackgroundRuntimeText.Text = background
            ? "This desktop process may remain open; no background service is installed."
            : "Closing Soltex ends this desktop process; no background service is installed.";
        BackgroundRuntimeStateText.Text = background ? "OPT-IN" : "EXIT";
    }

    private void SetSelected(Button button, bool selected)
    {
        button.Background = (Brush)FindResource(
            selected ? "SelectedNavBrush" : "NavRestBrush");
        button.Foreground = (Brush)FindResource(
            selected ? "AccentBrush" : "MutedBrush");
    }

    private void ShowSettingsPanel(UIElement panel, Button selectedTab)
    {
        GeneralSettingsPanel.Visibility = panel == GeneralSettingsPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        WindowSettingsPanel.Visibility = panel == WindowSettingsPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        PrivacySettingsPanel.Visibility = panel == PrivacySettingsPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = panel == AboutSettingsPanel
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (Button tab in new[]
                 {
                     GeneralSettingsTab,
                     WindowSettingsTab,
                     PrivacySettingsTab,
                     AboutSettingsTab
                 })
        {
            tab.Tag = tab == selectedTab ? "Selected" : null;
        }
    }

    private void SetToggle(Button button, bool enabled)
    {
        button.Content = enabled ? "On" : "Off";
        SetSelected(button, enabled);
        button.SetCurrentValue(
            System.Windows.Automation.AutomationProperties.HelpTextProperty,
            enabled ? "Currently on" : "Currently off");
    }

    private void SetState(string label, string brushResource)
    {
        Brush brush = (Brush)FindResource(brushResource);
        SettingsStateDot.Fill = brush;
        SettingsStateText.Foreground = brush;
        SettingsStateText.Text = label;
    }
}
