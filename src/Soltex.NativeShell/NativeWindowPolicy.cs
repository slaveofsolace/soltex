namespace Soltex.NativeShell;

public enum NativeWindowBackdropKind
{
    Mica,
    Solid
}

public enum NativeWindowChromeKind
{
    AppWindow,
    System
}

public enum NativeWindowFallbackReason
{
    None,
    UnsupportedOperatingSystem,
    HighContrast,
    TransparencyDisabled,
    AppWindowUnavailable,
    TitleBarCustomizationUnavailable,
    NativeCallFailed
}

public sealed record NativeWindowCapabilityInputs(
    bool IsWindows11OrLater,
    bool IsHighContrast,
    bool IsTransparencyEnabled,
    bool IsAppWindowAvailable,
    bool IsTitleBarCustomizationAvailable);

public sealed record NativeWindowPolicyDecision(
    NativeWindowBackdropKind Backdrop,
    NativeWindowChromeKind Chrome,
    NativeWindowFallbackReason BackdropFallbackReason,
    NativeWindowFallbackReason ChromeFallbackReason)
{
    public bool UsesNativeTitleBar => Chrome == NativeWindowChromeKind.AppWindow;
}

public readonly record struct NativePixelRect(int X, int Y, int Width, int Height);

public static class NativeWindowPolicy
{
    private const int MaximumPixelExtent = 1_000_000;

    public static NativeWindowPolicyDecision Evaluate(NativeWindowCapabilityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        (NativeWindowBackdropKind backdrop, NativeWindowFallbackReason backdropReason) =
            EvaluateBackdrop(inputs);
        (NativeWindowChromeKind chrome, NativeWindowFallbackReason chromeReason) =
            EvaluateChrome(inputs);

        return new NativeWindowPolicyDecision(
            backdrop,
            chrome,
            backdropReason,
            chromeReason);
    }

    public static bool TryCreateDragRectangle(
        double leftDip,
        double topDip,
        double widthDip,
        double heightDip,
        double dpiScale,
        out NativePixelRect rectangle)
    {
        rectangle = default;

        if (!IsFiniteNonNegative(leftDip) ||
            !IsFiniteNonNegative(topDip) ||
            !IsFinitePositive(widthDip) ||
            !IsFinitePositive(heightDip) ||
            !IsFinitePositive(dpiScale))
        {
            return false;
        }

        double left = Math.Round(leftDip * dpiScale, MidpointRounding.AwayFromZero);
        double top = Math.Round(topDip * dpiScale, MidpointRounding.AwayFromZero);
        double width = Math.Round(widthDip * dpiScale, MidpointRounding.AwayFromZero);
        double height = Math.Round(heightDip * dpiScale, MidpointRounding.AwayFromZero);

        if (!IsBoundedPixelValue(left) ||
            !IsBoundedPixelValue(top) ||
            !IsBoundedPositivePixelValue(width) ||
            !IsBoundedPositivePixelValue(height))
        {
            return false;
        }

        rectangle = new NativePixelRect(
            checked((int)left),
            checked((int)top),
            checked((int)width),
            checked((int)height));
        return true;
    }

    private static (NativeWindowBackdropKind Kind, NativeWindowFallbackReason Reason)
        EvaluateBackdrop(NativeWindowCapabilityInputs inputs)
    {
        if (!inputs.IsWindows11OrLater)
        {
            return (NativeWindowBackdropKind.Solid, NativeWindowFallbackReason.UnsupportedOperatingSystem);
        }

        if (inputs.IsHighContrast)
        {
            return (NativeWindowBackdropKind.Solid, NativeWindowFallbackReason.HighContrast);
        }

        if (!inputs.IsTransparencyEnabled)
        {
            return (NativeWindowBackdropKind.Solid, NativeWindowFallbackReason.TransparencyDisabled);
        }

        return (NativeWindowBackdropKind.Mica, NativeWindowFallbackReason.None);
    }

    private static (NativeWindowChromeKind Kind, NativeWindowFallbackReason Reason)
        EvaluateChrome(NativeWindowCapabilityInputs inputs)
    {
        if (!inputs.IsWindows11OrLater)
        {
            return (NativeWindowChromeKind.System, NativeWindowFallbackReason.UnsupportedOperatingSystem);
        }

        if (!inputs.IsAppWindowAvailable)
        {
            return (NativeWindowChromeKind.System, NativeWindowFallbackReason.AppWindowUnavailable);
        }

        if (!inputs.IsTitleBarCustomizationAvailable)
        {
            return (NativeWindowChromeKind.System, NativeWindowFallbackReason.TitleBarCustomizationUnavailable);
        }

        return (NativeWindowChromeKind.AppWindow, NativeWindowFallbackReason.None);
    }

    private static bool IsFiniteNonNegative(double value) =>
        double.IsFinite(value) && value >= 0;

    private static bool IsFinitePositive(double value) =>
        double.IsFinite(value) && value > 0;

    private static bool IsBoundedPixelValue(double value) =>
        value >= 0 && value <= MaximumPixelExtent;

    private static bool IsBoundedPositivePixelValue(double value) =>
        value >= 1 && value <= MaximumPixelExtent;
}
