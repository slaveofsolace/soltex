namespace Soltex.App;

internal enum RenderSmokeAppearance
{
    Dark,
    Light,
    HighContrast
}

internal sealed record RenderSmokeRequest(
    string OutputPath,
    string? Panel,
    RenderSmokeAppearance Appearance);

internal static class RuntimeLaunchPolicy
{
    internal static TimeSpan RenderSmokeStartupTimeout { get; } = TimeSpan.FromSeconds(40);

    internal static bool IsRuntimeProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 &&
        CountOption(arguments, "--render-smoke") == 0 &&
        CountOption(arguments, "--whisper-runtime-probe") == 0;

    internal static bool IsWhisperRuntimeProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--whisper-runtime-probe") == 1 &&
        CountOption(arguments, "--runtime-probe") == 0 &&
        CountOption(arguments, "--render-smoke") == 0;

    internal static bool UsesControlledRuntime(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 ||
        CountOption(arguments, "--render-smoke") == 1 ||
        CountOption(arguments, "--whisper-runtime-probe") == 1;

    internal static bool UsesSoftwareRendering(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--render-smoke") == 1 &&
        CountOption(arguments, "--runtime-probe") == 0;

    internal static bool HasControlledRuntimeOption(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") > 0 ||
        CountOption(arguments, "--whisper-runtime-probe") > 0 ||
        CountOption(arguments, "--render-smoke") > 0 ||
        CountOption(arguments, "--panel") > 0 ||
        CountOption(arguments, "--theme") > 0;

    internal static bool TryParseRenderSmoke(
        IReadOnlyList<string> arguments,
        out RenderSmokeRequest? request)
    {
        request = null;
        if (CountOption(arguments, "--render-smoke") != 1 ||
            CountOption(arguments, "--runtime-probe") != 0 ||
            CountOption(arguments, "--whisper-runtime-probe") != 0)
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
        bool appearanceSpecified = false;
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

            return false;
        }

        request = new RenderSmokeRequest(outputPath, panel, appearance);
        return true;
    }

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
}
