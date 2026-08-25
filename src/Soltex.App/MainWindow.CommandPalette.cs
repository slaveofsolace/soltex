using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Soltex.App;

public partial class MainWindow
{
    private IInputElement? _focusBeforeCommandPalette;

    private void CommandPaletteButton_Click(object sender, RoutedEventArgs e) =>
        OpenCommandPalette();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleOnboardingShortcut(e))
        {
            return;
        }

        if (e.Key == Key.Escape && CommandPaletteOverlay.Visibility == Visibility.Visible)
        {
            CloseCommandPalette(restoreFocus: true);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            if (CommandPaletteOverlay.Visibility == Visibility.Visible)
            {
                CloseCommandPalette(restoreFocus: true);
            }
            else
            {
                OpenCommandPalette();
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control ||
            CommandPaletteOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        string? workspace = e.Key switch
        {
            Key.D1 or Key.NumPad1 => "home",
            Key.D2 or Key.NumPad2 => "monitoring",
            Key.D3 or Key.NumPad3 => "applications",
            Key.D4 or Key.NumPad4 => "mixer",
            Key.D5 or Key.NumPad5 => "security",
            Key.D6 or Key.NumPad6 => "remote",
            Key.D7 or Key.NumPad7 => "activity",
            Key.D8 or Key.NumPad8 => "updates",
            Key.D9 or Key.NumPad9 => "settings",
            Key.D0 or Key.NumPad0 => "whisper",
            _ => null
        };
        if (workspace is null)
        {
            return;
        }

        NavigateToWorkspace(workspace);
        e.Handled = true;
    }

    private void OpenCommandPalette()
    {
        _focusBeforeCommandPalette = Keyboard.FocusedElement;
        CommandSearchBox.Text = string.Empty;
        UpdateCommandPaletteResults();
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandSearchBox.Focus();
        Keyboard.Focus(CommandSearchBox);
    }

    private void CloseCommandPalette(bool restoreFocus)
    {
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        if (restoreFocus && _focusBeforeCommandPalette is IInputElement focusTarget)
        {
            Keyboard.Focus(focusTarget);
        }

        _focusBeforeCommandPalette = null;
    }

    private void CommandSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateCommandPaletteResults();

    private void UpdateCommandPaletteResults()
    {
        IReadOnlyList<WorkspaceCommand> results =
            WorkspaceCommandCatalog.Query(CommandSearchBox.Text);
        CommandPaletteList.ItemsSource = results;
        CommandEmptyText.Visibility = results.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CommandPaletteList.SelectedIndex = results.Count == 0 ? -1 : 0;
    }

    private void CommandSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && CommandPaletteList.Items.Count > 0)
        {
            CommandPaletteList.Focus();
            CommandPaletteList.SelectedIndex = Math.Max(0, CommandPaletteList.SelectedIndex);
            if (CommandPaletteList.ItemContainerGenerator.ContainerFromIndex(
                    CommandPaletteList.SelectedIndex) is ListBoxItem item)
            {
                item.Focus();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenSelectedCommand();
            e.Handled = true;
        }
    }

    private void CommandPaletteList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && CommandPaletteList.SelectedIndex <= 0)
        {
            CommandSearchBox.Focus();
            Keyboard.Focus(CommandSearchBox);
            e.Handled = true;
        }
    }

    private void CommandPaletteList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        OpenSelectedCommand();

    private void OpenSelectedCommand()
    {
        if (CommandPaletteList.SelectedItem is not WorkspaceCommand command)
        {
            return;
        }

        CloseCommandPalette(restoreFocus: false);
        NavigateToWorkspace(command.Workspace);
    }

    private void CommandPaletteOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            CloseCommandPalette(restoreFocus: true);
            e.Handled = true;
        }
    }

    private void CommandPaletteSurface_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) => e.Handled = true;

    private void NavigateToWorkspace(string workspace)
    {
        _ = WorkspaceNavigationPolicy.TryResolve(workspace, out WorkspaceNavigationTarget target);
        Button button = target switch
        {
            WorkspaceNavigationTarget.Monitoring => MonitoringNavButton,
            WorkspaceNavigationTarget.Applications => ApplicationsNavButton,
            WorkspaceNavigationTarget.Mixer => MixerNavButton,
            WorkspaceNavigationTarget.Security => SecurityNavButton,
            WorkspaceNavigationTarget.Remote => RemoteNavButton,
            WorkspaceNavigationTarget.Whisper => WhisperNavButton,
            WorkspaceNavigationTarget.Activity => ActivityNavButton,
            WorkspaceNavigationTarget.Updates => UpdateNavButton,
            WorkspaceNavigationTarget.Settings => SettingsNavButton,
            _ => HomeNavButton
        };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.Focus();
    }
}
