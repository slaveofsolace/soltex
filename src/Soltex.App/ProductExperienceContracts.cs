namespace Soltex.App;

/// <summary>
/// Selects how Soltex resolves its light or dark application palette.
/// </summary>
public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Selects the source of the interaction accent. Status colours remain semantic.
/// </summary>
public enum ThemeAccent
{
    SoltexGlacier,
    Windows
}

/// <summary>
/// Controls information density without changing capability or hiding state.
/// </summary>
public enum InterfaceDensity
{
    Comfortable,
    Compact
}

/// <summary>
/// Immutable appearance contract shared by settings, the shell, and native adapters.
/// </summary>
public readonly record struct ThemeProfile(
    ThemeMode Mode,
    ThemeAccent Accent,
    InterfaceDensity Density)
{
    public static ThemeProfile Default { get; } =
        new(ThemeMode.System, ThemeAccent.SoltexGlacier, InterfaceDensity.Comfortable);

    public ThemeProfile Normalize() =>
        new(
            Enum.IsDefined(Mode) ? Mode : ThemeMode.System,
            Enum.IsDefined(Accent) ? Accent : ThemeAccent.SoltexGlacier,
            Enum.IsDefined(Density) ? Density : InterfaceDensity.Comfortable);
}

/// <summary>
/// Truthful availability state for a visible Soltex capability.
/// </summary>
public enum FeatureCapabilityState
{
    Available,
    Degraded,
    Unsupported,
    ConsentRequired
}

/// <summary>
/// Path-free capability state suitable for presentation and local diagnostics.
/// </summary>
public sealed record FeatureCapability
{
    public const int MaximumFeatureIdLength = 64;
    public const int MaximumDetailLength = 240;
    public const int MaximumNextActionLength = 120;

    private FeatureCapability(
        string featureId,
        FeatureCapabilityState state,
        string detail,
        string nextAction)
    {
        FeatureId = featureId;
        State = state;
        Detail = detail;
        NextAction = nextAction;
    }

    public string FeatureId { get; }

    public FeatureCapabilityState State { get; }

    public string Detail { get; }

    public string NextAction { get; }

    public static FeatureCapability Create(
        string featureId,
        FeatureCapabilityState state,
        string detail,
        string? nextAction = null)
    {
        string normalizedId = NormalizeFeatureId(featureId);
        FeatureCapabilityState normalizedState = Enum.IsDefined(state)
            ? state
            : FeatureCapabilityState.Unsupported;
        return new FeatureCapability(
            normalizedId,
            normalizedState,
            NormalizeDisplayText(detail, MaximumDetailLength, "Capability detail is unavailable."),
            NormalizeDisplayText(nextAction, MaximumNextActionLength, string.Empty));
    }

    private static string NormalizeFeatureId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string candidate = value.Trim().ToLowerInvariant();
        if (candidate.Length > MaximumFeatureIdLength ||
            candidate.Any(character =>
                character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-'))
        {
            throw new ArgumentException(
                "Feature identifiers may contain only lowercase letters, digits, dots, and hyphens.",
                nameof(value));
        }

        return candidate;
    }

    private static string NormalizeDisplayText(string? value, int maximumLength, string fallback)
    {
        string candidate = string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (candidate.Length == 0)
        {
            return fallback;
        }

        return candidate.Length <= maximumLength
            ? candidate
            : candidate[..maximumLength];
    }
}

[Flags]
public enum OnboardingArea
{
    None = 0,
    PrivacyAndLocalData = 1 << 0,
    Appearance = 1 << 1,
    AudioDiscovery = 1 << 2,
    Whisper = 1 << 3,
    CaptureStorage = 1 << 4,
    Shortcuts = 1 << 5,
    AdvancedLabSafety = 1 << 6,
    All = PrivacyAndLocalData |
          Appearance |
          AudioDiscovery |
          Whisper |
          CaptureStorage |
          Shortcuts |
          AdvancedLabSafety
}

/// <summary>
/// Versioned first-run progress. Completion is valid only after every V1 area was reviewed.
/// </summary>
public readonly record struct OnboardingState(
    int ContractVersion,
    OnboardingArea CompletedAreas,
    bool IsCompleted,
    DateTimeOffset? CompletedAtUtc)
{
    public const int CurrentContractVersion = 1;

    public static OnboardingState Default { get; } =
        new(CurrentContractVersion, OnboardingArea.None, false, null);

    public OnboardingState Normalize()
    {
        OnboardingArea knownAreas = CompletedAreas & OnboardingArea.All;
        bool completed = IsCompleted && knownAreas == OnboardingArea.All;
        return new OnboardingState(
            CurrentContractVersion,
            knownAreas,
            completed,
            completed ? CompletedAtUtc?.ToUniversalTime() : null);
    }

    public OnboardingState MarkReviewed(OnboardingArea area)
    {
        if (area == OnboardingArea.None || (area & ~OnboardingArea.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        return (this with
        {
            CompletedAreas = CompletedAreas | area,
            IsCompleted = false,
            CompletedAtUtc = null
        }).Normalize();
    }

    public OnboardingState Complete(DateTimeOffset completedAtUtc)
    {
        OnboardingState normalized = Normalize();
        if (normalized.CompletedAreas != OnboardingArea.All)
        {
            throw new InvalidOperationException(
                "Every onboarding area must be reviewed before setup can be completed.");
        }

        return normalized with
        {
            IsCompleted = true,
            CompletedAtUtc = completedAtUtc.ToUniversalTime()
        };
    }
}
