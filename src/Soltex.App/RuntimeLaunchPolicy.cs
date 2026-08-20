namespace Soltex.App;

internal enum RenderSmokeAppearance
{
    Dark,
    Light,
    HighContrast
}

internal enum RenderSmokeProfile
{
    Standard100,
    Compact100,
    Compact150,
    Compact200
}

internal sealed record RenderSmokeViewport(
    int LogicalWidth,
    int LogicalHeight,
    int ScalePercent)
{
    internal int PixelWidth => checked(LogicalWidth * ScalePercent / 100);

    internal int PixelHeight => checked(LogicalHeight * ScalePercent / 100);

    internal double Dpi => 96d * ScalePercent / 100d;

    internal bool IsSyntheticHighDensity => ScalePercent != 100;
}

internal sealed record RenderSmokeRequest(
    string OutputPath,
    string? Panel,
    RenderSmokeAppearance Appearance,
    RenderSmokeProfile Profile);

internal sealed record WhisperOverlaySmokeRequest(
    string OutputPath,
    string State,
    RenderSmokeAppearance Appearance,
    int ScalePercent);

internal static class RuntimeLaunchPolicy
{
    internal static TimeSpan RenderSmokeStartupTimeout { get; } = TimeSpan.FromSeconds(40);

    internal static bool IsRuntimeProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 &&
        CountOption(arguments, "--render-smoke") == 0 &&
        CountOption(arguments, "--whisper-runtime-probe") == 0 &&
        CountOption(arguments, "--whisper-model-probe") == 0 &&
        CountOption(arguments, "--whisper-overlay-smoke") == 0 &&
        CountOption(arguments, "--whisper-uninstall-cleanup") == 0;

    internal static bool IsWhisperRuntimeProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--whisper-runtime-probe") == 1 &&
        CountOption(arguments, "--runtime-probe") == 0 &&
        CountOption(arguments, "--render-smoke") == 0 &&
        CountOption(arguments, "--whisper-model-probe") == 0 &&
        CountOption(arguments, "--whisper-overlay-smoke") == 0 &&
        CountOption(arguments, "--whisper-uninstall-cleanup") == 0;

    internal static bool IsWhisperModelProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--whisper-model-probe") == 1 &&
        CountOption(arguments, "--runtime-probe") == 0 &&
        CountOption(arguments, "--render-smoke") == 0 &&
        CountOption(arguments, "--whisper-runtime-probe") == 0 &&
        CountOption(arguments, "--whisper-overlay-smoke") == 0 &&
        CountOption(arguments, "--whisper-uninstall-cleanup") == 0;

    internal static bool IsWhisperUninstallCleanup(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 &&
        CountOption(arguments, "--whisper-uninstall-cleanup") == 1;

    internal static bool UsesControlledRuntime(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 ||
        CountOption(arguments, "--render-smoke") == 1 ||
        CountOption(arguments, "--whisper-runtime-probe") == 1 ||
        CountOption(arguments, "--whisper-model-probe") == 1 ||
        CountOption(arguments, "--whisper-overlay-smoke") == 1 ||
        CountOption(arguments, "--whisper-uninstall-cleanup") == 1;

    internal static bool UsesSoftwareRendering(IReadOnlyList<string> arguments) =>
        (CountOption(arguments, "--render-smoke") == 1 ||
         CountOption(arguments, "--whisper-overlay-smoke") == 1) &&
        CountOption(arguments, "--runtime-probe") == 0 &&
        CountOption(arguments, "--whisper-runtime-probe") == 0 &&
        CountOption(arguments, "--whisper-model-probe") == 0 &&
        CountOption(arguments, "--whisper-uninstall-cleanup") == 0;

    internal static bool HasControlledRuntimeOption(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") > 0 ||
        CountOption(arguments, "--whisper-runtime-probe") > 0 ||
        CountOption(arguments, "--whisper-model-probe") > 0 ||
        CountOption(arguments, "--whisper-overlay-smoke") > 0 ||
        CountOption(arguments, "--whisper-uninstall-cleanup") > 0 ||
        CountOption(arguments, "--render-smoke") > 0 ||
        CountOption(arguments, "--panel") > 0 ||
        CountOption(arguments, "--theme") > 0 ||
        CountOption(arguments, "--profile") > 0 ||
        CountOption(arguments, "--state") > 0 ||
        CountOption(arguments, "--density") > 0;

    internal static bool TryParseRenderSmoke(
        IReadOnlyList<string> arguments,
        out RenderSmokeRequest? request)
    {
        request = null;
        if (CountOption(arguments, "--render-smoke") != 1 ||
            CountOption(arguments, "--runtime-probe") != 0 ||
            CountOption(arguments, "--whisper-runtime-probe") != 0 ||
            CountOption(arguments, "--whisper-model-probe") != 0 ||
            CountOption(arguments, "--whisper-overlay-smoke") != 0 ||
            CountOption(arguments, "--whisper-uninstall-cleanup") != 0)
        {
            return false;
        }

        int renderIndex = IndexOf(arguments, "--render-smoke");
        if (renderIndex < 0 || renderIndex + 1 >= arguments.Count ||
            IsOption(arguments[renderIndex + 1]))
        {
            return false;
        }

        string outputPath = arguments[renderIndex + 1];
        string? panel = null;
        RenderSmokeAppearance appearance = RenderSmokeAppearance.Dark;
        RenderSmokeProfile profile = RenderSmokeProfile.Standard100;
        bool appearanceSpecified = false;
        bool profileSpecified = false;
        for (int index = renderIndex + 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || IsOption(arguments[index + 1]))
            {
                return false;
            }

            if (string.Equals(arguments[index], "--panel", StringComparison.OrdinalIgnoreCase))
            {
                if (panel is not null)
                {
                    return false;
                }

                panel = arguments[index + 1];
                continue;
            }

            if (string.Equals(arguments[index], "--theme", StringComparison.OrdinalIgnoreCase))
            {
                if (appearanceSpecified ||
                    !TryParseAppearance(arguments[index + 1], out appearance))
                {
                    return false;
                }

                appearanceSpecified = true;
                continue;
            }

            if (string.Equals(arguments[index], "--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (profileSpecified ||
                    !TryParseProfile(arguments[index + 1], out profile))
                {
                    return false;
                }

                profileSpecified = true;
                continue;
            }

            return false;
        }

        request = new RenderSmokeRequest(outputPath, panel, appearance, profile);
        return true;
    }

    internal static bool TryParseWhisperOverlaySmoke(
        IReadOnlyList<string> arguments,
        out WhisperOverlaySmokeRequest? request)
    {
        request = null;
        if (CountOption(arguments, "--whisper-overlay-smoke") != 1 ||
            CountOption(arguments, "--runtime-probe") != 0 ||
            CountOption(arguments, "--whisper-runtime-probe") != 0 ||
            CountOption(arguments, "--whisper-model-probe") != 0 ||
            CountOption(arguments, "--render-smoke") != 0 ||
            CountOption(arguments, "--whisper-uninstall-cleanup") != 0)
        {
            return false;
        }

        int overlayIndex = IndexOf(arguments, "--whisper-overlay-smoke");
        if (overlayIndex < 0 || overlayIndex + 1 >= arguments.Count ||
            IsOption(arguments[overlayIndex + 1]))
        {
            return false;
        }

        string outputPath = arguments[overlayIndex + 1];
        string? state = null;
        RenderSmokeAppearance appearance = RenderSmokeAppearance.Dark;
        int scalePercent = 100;
        bool appearanceSpecified = false;
        bool densitySpecified = false;
        for (int index = overlayIndex + 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || IsOption(arguments[index + 1]))
            {
                return false;
            }

            if (string.Equals(arguments[index], "--state", StringComparison.OrdinalIgnoreCase))
            {
                if (state is not null || !IsStableStateId(arguments[index + 1]))
                {
                    return false;
                }

                state = arguments[index + 1].ToLowerInvariant();
                continue;
            }

            if (string.Equals(arguments[index], "--theme", StringComparison.OrdinalIgnoreCase))
            {
                if (appearanceSpecified ||
                    !TryParseAppearance(arguments[index + 1], out appearance))
                {
                    return false;
                }

                appearanceSpecified = true;
                continue;
            }

            if (string.Equals(arguments[index], "--density", StringComparison.OrdinalIgnoreCase))
            {
                if (densitySpecified ||
                    !TryParseDensity(arguments[index + 1], out scalePercent))
                {
                    return false;
                }

                densitySpecified = true;
                continue;
            }

            return false;
        }

        if (state is null)
        {
            return false;
        }

        request = new WhisperOverlaySmokeRequest(
            outputPath,
            state,
            appearance,
            scalePercent);
        return true;
    }

    internal static RenderSmokeViewport ResolveRenderViewport(
        RenderSmokeProfile profile) =>
        profile switch
        {
            RenderSmokeProfile.Compact100 => new RenderSmokeViewport(1100, 720, 100),
            RenderSmokeProfile.Compact150 => new RenderSmokeViewport(1100, 720, 150),
            RenderSmokeProfile.Compact200 => new RenderSmokeViewport(1100, 720, 200),
            _ => new RenderSmokeViewport(1280, 820, 100)
        };

    internal static ResolvedAppearance ResolveRenderAppearance(
        RenderSmokeAppearance appearance) =>
        appearance switch
        {
            RenderSmokeAppearance.Light => ResolvedAppearance.Light,
            RenderSmokeAppearance.HighContrast => ResolvedAppearance.HighContrast,
            _ => ResolvedAppearance.Dark
        };

    internal static AppearancePreference ResolveRenderPreference(
        RenderSmokeAppearance appearance) =>
        appearance == RenderSmokeAppearance.Light
            ? AppearancePreference.Light
            : AppearancePreference.Dark;

    private static int CountOption(
        IReadOnlyList<string> arguments,
        string option) =>
        arguments.Count(argument => string.Equals(
            argument,
            option,
            StringComparison.OrdinalIgnoreCase));

    private static int IndexOf(
        IReadOnlyList<string> arguments,
        string option)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsOption(string value) =>
        value.StartsWith("--", StringComparison.Ordinal);

    private static bool TryParseAppearance(
        string value,
        out RenderSmokeAppearance appearance)
    {
        appearance = value.ToLowerInvariant() switch
        {
            "dark" => RenderSmokeAppearance.Dark,
            "light" => RenderSmokeAppearance.Light,
            "high-contrast" => RenderSmokeAppearance.HighContrast,
            _ => (RenderSmokeAppearance)(-1)
        };
        return Enum.IsDefined(appearance);
    }

    private static bool TryParseProfile(
        string value,
        out RenderSmokeProfile profile)
    {
        profile = value.ToLowerInvariant() switch
        {
            "standard-100" => RenderSmokeProfile.Standard100,
            "compact-100" => RenderSmokeProfile.Compact100,
            "compact-150" => RenderSmokeProfile.Compact150,
            "compact-200" => RenderSmokeProfile.Compact200,
            _ => (RenderSmokeProfile)(-1)
        };
        return Enum.IsDefined(profile);
    }

    private static bool TryParseDensity(string value, out int scalePercent)
    {
        scalePercent = value switch
        {
            "100" => 100,
            "150" => 150,
            "200" => 200,
            _ => 0
        };
        return scalePercent != 0;
    }

    private static bool IsStableStateId(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
