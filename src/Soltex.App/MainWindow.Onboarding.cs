using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soltex.App.Views;

namespace Soltex.App;

public partial class MainWindow
{
    private IInputElement? _focusBeforeOnboarding;

    private void InitializeNativeShellExperience()
    {
        InitializeNativeWindowLifecycle();
        OnboardingPanel.ProgressChanged += OnboardingPanel_ProgressChanged;
        OnboardingPanel.Completed += OnboardingPanel_Completed;
        OnboardingPanel.ThemeChanged += OnboardingPanel_ThemeChanged;
        OnboardingPanel.DismissRequested += OnboardingPanel_DismissRequested;
        ApplyShellLayout(Width);
    }

    private void ShowOnboarding(int? requestedStepIndex = null)
    {
        _focusBeforeOnboarding = Keyboard.FocusedElement;
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        OnboardingPanel.ShowState(
            _preferences.OnboardingState,
            _preferences.ThemeProfile,
            requestedStepIndex);
        OnboardingOverlay.Visibility = Visibility.Visible;
    }

    private void HideOnboarding(bool restoreFocus)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        if (restoreFocus && _focusBeforeOnboarding is IInputElement focusTarget)
        {
            Keyboard.Focus(focusTarget);
        }
        else
        {
            HomeNavButton.Focus();
        }

        _focusBeforeOnboarding = null;
    }

    private void OnboardingPanel_ProgressChanged(
        object? sender,
        OnboardingProgressEventArgs e)
    {
        _preferences = _preferences with { OnboardingState = e.State };
        PersistOnboardingPreferences();
    }

    private void OnboardingPanel_Completed(
        object? sender,
        OnboardingProgressEventArgs e)
    {
        _preferences = _preferences with { OnboardingState = e.State };
        PersistOnboardingPreferences();
        SettingsPanel.UpdatePreferences(
            _preferences,
            recoveredFromInvalid: false,
            "First-run setup is complete.");
        HideOnboarding(restoreFocus: false);
    }

    private void OnboardingPanel_ThemeChanged(
        object? sender,
        OnboardingThemeChangedEventArgs e)
    {
        ThemeProfile profile = e.Profile.Normalize();
        _preferences = _preferences with
        {
            AppearancePreference = profile.Mode switch
            {
                global::Soltex.App.ThemeMode.Light => AppearancePreference.Light,
                global::Soltex.App.ThemeMode.Dark => AppearancePreference.Dark,
                _ => AppearancePreference.System
            },
            ThemeAccent = profile.Accent,
            InterfaceDensity = profile.Density
        };
        if (Application.Current is App application)
        {
            application.SetThemeProfile(profile);
        }

        SettingsPanel.UpdatePreferences(
            _preferences,
            recoveredFromInvalid: false,
            "Appearance updated from first-run setup.");
        ApplyShellLayout(ActualWidth);
        PersistOnboardingPreferences();
    }

    private void OnboardingPanel_DismissRequested(object? sender, EventArgs e) =>
        HideOnboarding(restoreFocus: true);

    private void PersistOnboardingPreferences()
    {
        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (IsExpectedPreferenceWriteFailure(exception))
        {
            SettingsPanel.ShowSaveFailure();
        }
    }

    private bool TryHandleOnboardingShortcut(KeyEventArgs e)
    {
        if (OnboardingOverlay.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            HideOnboarding(restoreFocus: true);
            e.Handled = true;
            return true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control &&
            (e.Key == Key.K ||
             e.Key is >= Key.D0 and <= Key.D9 ||
             e.Key is >= Key.NumPad0 and <= Key.NumPad9))
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyShellLayout(e.NewSize.Width);
        QueueNativeDragRegionUpdate();
    }

    private void ApplyShellLayout(double availableWidth)
    {
        ShellLayout layout = ShellLayoutPolicy.Resolve(
            availableWidth,
            _preferences.ThemeProfile.Density);
        NavigationColumn.Width = new GridLength(layout.NavigationWidth);
        WorkspaceHost.Margin = layout.WorkspaceMargin;
        BrandTextPanel.Visibility = layout.ShowBrandText
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProtectionSummaryBorder.Visibility = layout.ShowProtectionSummary
            ? Visibility.Visible
            : Visibility.Collapsed;
        NavigationFooter.Visibility = layout.ShowNavigationFooter
            ? Visibility.Visible
            : Visibility.Collapsed;
        NavigationRailContent.Margin = layout.Mode switch
        {
            ShellLayoutMode.Narrow => new Thickness(3d, 12d, 3d, 12d),
            ShellLayoutMode.Compact => new Thickness(8d, 16d, 8d, 16d),
            _ => new Thickness(14d, 18d, 14d, 18d)
        };

        Visibility labelVisibility = layout.ShowNavigationLabels
            ? Visibility.Visible
            : Visibility.Collapsed;
        foreach (TextBlock text in FindVisualChildren<TextBlock>(NavigationRail))
        {
            if (Equals(text.Tag, "NavLabel") ||
                Equals(text.Tag, "NavDetail") ||
                Equals(text.Tag, "NavGroup"))
            {
                text.Visibility = labelVisibility;
            }
        }

        foreach (Button button in NavigationButtons())
        {
            button.HorizontalContentAlignment = layout.ShowNavigationLabels
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
        }

        SecurityNavButton.ToolTip = layout.ShowProtectionSummary
            ? null
            : $"{ProtectionSummaryTitle.Text}: {ProtectionSummaryDetail.Text}";
    }

    private IEnumerable<Button> NavigationButtons()
    {
        yield return HomeNavButton;
        yield return MonitoringNavButton;
        yield return ApplicationsNavButton;
        yield return MixerNavButton;
        yield return SecurityNavButton;
        yield return WhisperNavButton;
        yield return RemoteNavButton;
        yield return DevicesNavButton;
        yield return ClipsNavButton;
        yield return ActivityNavButton;
        yield return UpdateNavButton;
        yield return CommandPaletteButton;
        yield return SettingsNavButton;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int children = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < children; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
