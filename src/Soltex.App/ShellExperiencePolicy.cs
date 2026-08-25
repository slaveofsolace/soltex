using System.Windows;

namespace Soltex.App;

internal enum ShellLayoutMode
{
    Expanded,
    Compact,
    Narrow
}

internal readonly record struct ShellLayout(
    ShellLayoutMode Mode,
    double NavigationWidth,
    Thickness WorkspaceMargin,
    bool ShowNavigationLabels,
    bool ShowProtectionSummary,
    bool ShowBrandText,
    bool ShowNavigationFooter);

internal static class ShellLayoutPolicy
{
    internal const double ExpandedBreakpoint = 1180d;
    internal const double CompactBreakpoint = 940d;

    internal static ShellLayout Resolve(double availableWidth, InterfaceDensity density)
    {
        double width = double.IsFinite(availableWidth) && availableWidth > 0d
            ? availableWidth
            : ExpandedBreakpoint;
        InterfaceDensity normalizedDensity = Enum.IsDefined(density)
            ? density
            : InterfaceDensity.Comfortable;

        if (width < CompactBreakpoint)
        {
            return new ShellLayout(
                ShellLayoutMode.Narrow,
                64d,
                new Thickness(14d, 18d, 14d, 18d),
                ShowNavigationLabels: false,
                ShowProtectionSummary: false,
                ShowBrandText: false,
                ShowNavigationFooter: false);
        }

        if (width < ExpandedBreakpoint || normalizedDensity == InterfaceDensity.Compact)
        {
            return new ShellLayout(
                ShellLayoutMode.Compact,
                76d,
                new Thickness(20d, 20d, 20d, 20d),
                ShowNavigationLabels: false,
                ShowProtectionSummary: false,
                ShowBrandText: false,
                ShowNavigationFooter: false);
        }

        return new ShellLayout(
            ShellLayoutMode.Expanded,
            220d,
            new Thickness(28d, 22d, 28d, 24d),
            ShowNavigationLabels: true,
            ShowProtectionSummary: true,
            ShowBrandText: true,
            ShowNavigationFooter: true);
    }
}

internal sealed record OnboardingStepDefinition(
    OnboardingArea Area,
    string Eyebrow,
    string Title,
    string Detail,
    string SafeDefault,
    FeatureCapability Capability);

internal static class OnboardingExperienceCatalog
{
    internal static IReadOnlyList<OnboardingStepDefinition> All { get; } =
    [
        new(
            OnboardingArea.PrivacyAndLocalData,
            "1 · PRIVACY",
            "Local by default",
            "Soltex stores preferences and owned evidence on this Windows account. Diagnostics stay opt-in, and Activity never receives Whisper transcripts or captured media.",
            "Keep local diagnostics off until you explicitly export them.",
            FeatureCapability.Create(
                "onboarding.local-data",
                FeatureCapabilityState.Available,
                "Local preference and evidence boundaries are active.",
                "Review the boundary, then continue.")),
        new(
            OnboardingArea.Appearance,
            "2 · APPEARANCE",
            "Make the instrument yours",
            "Choose the application theme, interaction accent, and information density. Safety and warning colours always keep their semantic meaning.",
            "Follow Windows, use Soltex Glacier, and keep comfortable spacing.",
            FeatureCapability.Create(
                "onboarding.appearance",
                FeatureCapabilityState.Available,
                "Theme, accent, and density update live and persist transactionally.",
                "Choose a profile or keep the safe default.")),
        new(
            OnboardingArea.AudioDiscovery,
            "3 · AUDIO",
            "Discover without taking over",
            "Soltex can observe documented Windows audio endpoints and sessions. It will not replace virtual devices, rewrite Sonar configuration, or claim system-wide processing.",
            "Leave routing unchanged and review detected endpoints in Audio.",
            FeatureCapability.Create(
                "onboarding.audio",
                FeatureCapabilityState.Degraded,
                "Endpoint and session controls exist; Sonar-aware discovery and Soltex-owned processing are still being completed.",
                "Continue with existing Windows routing.")),
        new(
            OnboardingArea.Whisper,
            "4 · WHISPER",
            "Dictation stays fail-closed",
            "Whisper uses a local model and explicit microphone access. Auto-send remains off unless target inspection, insertion verification, and the submit gate all authorize it.",
            "Keep capture idle and auto-send off until the guided physical check.",
            FeatureCapability.Create(
                "onboarding.whisper",
                FeatureCapabilityState.ConsentRequired,
                "The local provider exists, but microphone and target proof require your final guided session.",
                "Grant access only when you are ready to test.")),
        new(
            OnboardingArea.CaptureStorage,
            "5 · CAPTURE",
            "Recording is never ambient",
            "Capture will use an explicit picker, a persistent recording indicator, bounded storage, and replay disabled by default.",
            "Keep recording and instant replay off.",
            FeatureCapability.Create(
                "onboarding.capture",
                FeatureCapabilityState.Unsupported,
                "Production recording, replay, and the clips library have not landed yet.",
                "Leave Capture disabled until its verified workspace arrives.")),
        new(
            OnboardingArea.Shortcuts,
            "6 · SHORTCUTS",
            "Shortcuts stay intentional",
            "Global shortcuts register only after validation, do minimal hook work, and unregister during clean shutdown. Escape remains the universal reversible cancel path.",
            "Use the reviewed defaults and avoid reserved Windows chords.",
            FeatureCapability.Create(
                "onboarding.shortcuts",
                FeatureCapabilityState.ConsentRequired,
                "Whisper shortcut mechanisms exist; physical latency and keyboard-only acceptance remain open.",
                "Confirm shortcuts during the final owner session.")),
        new(
            OnboardingArea.AdvancedLabSafety,
            "7 · ADVANCED LAB",
            "No blind system tweaks",
            "Advanced actions will require compatibility detection, an exact preview, captured prior state, verification, an audit receipt, and tested rollback.",
            "Keep Advanced Lab unavailable until recipe rollback is proven in a disposable VM.",
            FeatureCapability.Create(
                "onboarding.advanced-lab",
                FeatureCapabilityState.Unsupported,
                "Versioned mutation recipes and VM recovery proof are not implemented yet.",
                "Use guidance-only workflows for now."))
    ];

    internal static int FindFirstIncompleteIndex(OnboardingState state)
    {
        OnboardingState normalized = state.Normalize();
        for (int index = 0; index < All.Count; index++)
        {
            if ((normalized.CompletedAreas & All[index].Area) == 0)
            {
                return index;
            }
        }

        return Math.Max(0, All.Count - 1);
    }
}
