using System.Windows;
using System.Windows.Media;
using Soltex.Whisper;

namespace Soltex.App;

/// <summary>
/// The listening surface. It is a renderer only: every decision about what to show
/// comes from <see cref="WhisperOverlayPresenter"/>, so the states this window can be
/// in are exactly the states covered by the Whisper core suite.
/// </summary>
public partial class WhisperOverlayWindow : Window
{
    public WhisperOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user presses the overlay's single action.</summary>
    public event EventHandler? ActionRequested;

    /// <summary>
    /// Draws one presenter frame. A hidden state closes the window rather than
    /// leaving an empty shell floating over the user's work.
    /// </summary>
    public void Render(WhisperOverlayView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!view.IsVisible)
        {
            Hide();
            return;
        }

        StateRail.Background = ResolveTone(view.Tone);
        HeadlineText.Text = view.Headline;
        TargetText.Text = view.TargetLabel;

        ElapsedText.Text = view.ElapsedText ?? string.Empty;
        ElapsedText.Visibility = view.ShowElapsed && view.ElapsedText is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        LevelMeter.Visibility = view.ShowLevelMeter ? Visibility.Visible : Visibility.Collapsed;

        bool hasAction = view.ActionLabel is { Length: > 0 };
        OverlayActionButton.Visibility = hasAction ? Visibility.Visible : Visibility.Collapsed;
        if (hasAction)
        {
            OverlayActionButton.Content = view.ActionLabel;
            System.Windows.Automation.AutomationProperties.SetName(
                OverlayActionButton,
                view.ActionLabel);
        }

        // The announcement is the accessible description of the whole surface, so a
        // screen-reader user hears the state change without the visual rail.
        System.Windows.Automation.AutomationProperties.SetHelpText(this, view.Announcement);

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Sets the live input level. Callers throttle this; the overlay does not sample
    /// audio itself and retains nothing.
    /// </summary>
    public void SetInputLevel(double level) =>
        LevelMeter.Value = Math.Clamp(level, 0d, 1d);

    private void OverlayAction_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, EventArgs.Empty);

    private static Brush ResolveTone(WhisperOverlayTone tone)
    {
        string key = tone switch
        {
            WhisperOverlayTone.Accent => "AccentBrush",
            WhisperOverlayTone.Signal => "SignalBrush",
            WhisperOverlayTone.Warning => "WarningBrush",
            WhisperOverlayTone.Danger => "DangerBrush",
            _ => "MutedBrush"
        };

        return (Brush)Application.Current.Resources[key];
    }
}
