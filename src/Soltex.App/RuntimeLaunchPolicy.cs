namespace Soltex.App;

internal static class RuntimeLaunchPolicy
{
    internal static bool IsRuntimeProbe(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 &&
        CountOption(arguments, "--render-smoke") == 0;

    internal static bool UsesControlledRuntime(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") == 1 ||
        CountOption(arguments, "--render-smoke") == 1;

    internal static bool UsesSoftwareRendering(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--render-smoke") == 1 &&
        CountOption(arguments, "--runtime-probe") == 0;

    internal static bool HasControlledRuntimeOption(IReadOnlyList<string> arguments) =>
        CountOption(arguments, "--runtime-probe") > 0 ||
        CountOption(arguments, "--render-smoke") > 0;

    private static int CountOption(
        IReadOnlyList<string> arguments,
        string option) =>
        arguments.Count(argument => string.Equals(
            argument,
            option,
            StringComparison.OrdinalIgnoreCase));
}
