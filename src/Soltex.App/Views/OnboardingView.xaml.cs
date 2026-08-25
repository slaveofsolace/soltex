using System.Windows;
using System.Windows.Controls;

namespace Soltex.App.Views;

public sealed class OnboardingProgressEventArgs(OnboardingState state) : EventArgs
{
    public OnboardingState State { get; } = state.Normalize();
}

public sealed class OnboardingThemeChangedEventArgs(ThemeProfile profile) : EventArgs
{
    public ThemeProfile Profile { get; } = profile.Normalize();
}

public partial class OnboardingView : UserControl
{
    private readonly IReadOnlyList<OnboardingStepDefinition> _steps =
        OnboardingExperienceCatalog.All;
    private OnboardingState _state = OnboardingState.Default;
    private ThemeProfile _profile = ThemeProfile.Default;
    private int _currentIndex;

    public OnboardingView()
    {
        InitializeComponent();
        RenderCurrentStep();
    }

    public event EventHandler<OnboardingProgressEventArgs>? ProgressChanged;

    public event EventHandler<OnboardingProgressEventArgs>? Completed;

    public event EventHandler<OnboardingThemeChangedEventArgs>? ThemeChanged;

    public event EventHandler? DismissRequested;

    internal FrameworkElement RenderFocusTarget => ContinueButton;

    internal void ShowState(
        OnboardingState state,
        ThemeProfile profile,
        int? requestedStepIndex = null)
    {
        _state = state.Normalize();
        _profile = profile.Normalize();
        _currentIndex = requestedStepIndex is >= 0 && requestedStepIndex < int.MaxValue
            ? Math.Min(requestedStepIndex.Value, _steps.Count - 1)
            : OnboardingExperienceCatalog.FindFirstIncompleteIndex(_state);
        RenderCurrentStep();
        ContinueButton.Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex <= 0)
        {
            return;
        }

        _currentIndex--;
        RenderCurrentStep();
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) =>
        DismissRequested?.Invoke(this, EventArgs.Empty);

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        OnboardingStepDefinition current = _steps[_currentIndex];
        _state = _state.MarkReviewed(current.Area);
        ProgressChanged?.Invoke(this, new OnboardingProgressEventArgs(_state));

        if (_currentIndex == _steps.Count - 1)
        {
            _state = _state.Complete(DateTimeOffset.UtcNow);
            Completed?.Invoke(this, new OnboardingProgressEventArgs(_state));
            return;
        }

        _currentIndex++;
        RenderCurrentStep();
    }

    private void SystemTheme_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Mode = global::Soltex.App.ThemeMode.System });

    private void LightTheme_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Mode = global::Soltex.App.ThemeMode.Light });

    private void DarkTheme_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Mode = global::Soltex.App.ThemeMode.Dark });

    private void GlacierAccent_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Accent = ThemeAccent.SoltexGlacier });

    private void WindowsAccent_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Accent = ThemeAccent.Windows });

    private void ComfortableDensity_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Density = InterfaceDensity.Comfortable });

    private void CompactDensity_Click(object sender, RoutedEventArgs e) =>
        UpdateTheme(_profile with { Density = InterfaceDensity.Compact });

    private void UpdateTheme(ThemeProfile requested)
    {
        _profile = requested.Normalize();
        UpdateAppearanceSelection();
        ThemeChanged?.Invoke(this, new OnboardingThemeChangedEventArgs(_profile));
    }

    private void RenderCurrentStep()
    {
        OnboardingStepDefinition step = _steps[_currentIndex];
        StepEyebrow.Text = step.Eyebrow;
        StepTitle.Text = step.Title;
        StepDetail.Text = step.Detail;
        SafeDefaultText.Text = step.SafeDefault;
        CapabilityDetailText.Text = step.Capability.Detail;
        CapabilityActionText.Text = step.Capability.NextAction;
        AppearanceOptions.Visibility = step.Area == OnboardingArea.Appearance
            ? Visibility.Visible
            : Visibility.Collapsed;
        BackButton.IsEnabled = _currentIndex > 0;
        ContinueButton.Content = _currentIndex == _steps.Count - 1
            ? "Finish setup"
            : "Continue";

        UpdateCapabilityState(step.Capability.State);
        UpdateAppearanceSelection();
        ProgressItems.ItemsSource = _steps
            .Select((candidate, index) => new OnboardingProgressRow(
                index + 1,
                candidate.Title,
                index == _currentIndex
                    ? "CURRENT"
                    : (_state.CompletedAreas & candidate.Area) != 0
                        ? "REVIEWED"
                        : "PENDING"))
            .ToArray();
    }

    private void UpdateCapabilityState(FeatureCapabilityState state)
    {
        (string label, string foreground, string background, string border) = state switch
        {
            FeatureCapabilityState.Available =>
                ("AVAILABLE", "SignalBrush", "SignalSurfaceBrush", "SignalBorderBrush"),
            FeatureCapabilityState.Degraded =>
                ("DEGRADED", "WarningBrush", "WarningSurfaceBrush", "WarningBorderBrush"),
            FeatureCapabilityState.ConsentRequired =>
                ("CONSENT REQUIRED", "AccentFocusBrush", "AccentQuietBrush", "AccentDimBrush"),
            _ =>
                ("UNAVAILABLE", "MutedBrush", "QuietBrush", "BorderBrush")
        };
        CapabilityStateText.Text = label;
        CapabilityStateText.SetResourceReference(ForegroundProperty, foreground);
        CapabilityPill.SetResourceReference(BackgroundProperty, background);
        CapabilityPill.SetResourceReference(Border.BorderBrushProperty, border);
    }

    private void UpdateAppearanceSelection()
    {
        SetSelected(
            SystemThemeButton,
            _profile.Mode == global::Soltex.App.ThemeMode.System);
        SetSelected(
            LightThemeButton,
            _profile.Mode == global::Soltex.App.ThemeMode.Light);
        SetSelected(
            DarkThemeButton,
            _profile.Mode == global::Soltex.App.ThemeMode.Dark);
        SetSelected(GlacierAccentButton, _profile.Accent == ThemeAccent.SoltexGlacier);
        SetSelected(WindowsAccentButton, _profile.Accent == ThemeAccent.Windows);
        SetSelected(
            ComfortableDensityButton,
            _profile.Density == InterfaceDensity.Comfortable);
        SetSelected(CompactDensityButton, _profile.Density == InterfaceDensity.Compact);
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.ClearValue(BackgroundProperty);
        button.ClearValue(Control.BorderBrushProperty);
        button.ClearValue(ForegroundProperty);
        if (!selected)
        {
            return;
        }

        button.SetResourceReference(BackgroundProperty, "AccentQuietBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        button.SetResourceReference(ForegroundProperty, "AccentFocusBrush");
    }

    private sealed record OnboardingProgressRow(
        int Number,
        string Title,
        string StateLabel);
}
