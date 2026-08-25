using System.Diagnostics;
using Soltex.NativeShell;

List<(string Name, Action Test)> tests =
[
    ("Windows 11 selects Mica and AppWindow", Windows11SelectsNativeExperience),
    ("High contrast selects a solid backdrop", HighContrastSelectsSolidBackdrop),
    ("Disabled transparency selects a solid backdrop", DisabledTransparencySelectsSolidBackdrop),
    ("Unsupported Windows selects system chrome", UnsupportedWindowsSelectsSystemChrome),
    ("Missing AppWindow selects system chrome", MissingAppWindowSelectsSystemChrome),
    ("Missing title bar customization selects system chrome", MissingTitleBarCustomizationSelectsSystemChrome),
    ("Drag geometry converts DIPs at 150 percent", DragGeometryConvertsDips),
    ("Invalid drag geometry fails closed", InvalidDragGeometryFailsClosed),
    ("Oversized drag geometry fails closed", OversizedDragGeometryFailsClosed),
    ("A zero window handle fails closed", ZeroWindowHandleFailsClosed),
    ("Null capability inputs are rejected", NullCapabilityInputsAreRejected)
];

int failed = 0;
Stopwatch suiteTimer = Stopwatch.StartNew();
foreach ((string name, Action test) in tests)
{
    Stopwatch testTimer = Stopwatch.StartNew();
    try
    {
        test();
        testTimer.Stop();
        Console.WriteLine($"PASS  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
    }
    catch (Exception exception)
    {
        testTimer.Stop();
        failed++;
        Console.WriteLine($"FAIL  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine("      " + exception.Message);
    }
}

suiteTimer.Stop();
Console.WriteLine();
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
Console.WriteLine($"MEASURE native_shell_suite tests={tests.Count} failed={failed} total_ms={suiteTimer.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static void Windows11SelectsNativeExperience()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(AvailableInputs());
    Equal(NativeWindowBackdropKind.Mica, decision.Backdrop);
    Equal(NativeWindowChromeKind.AppWindow, decision.Chrome);
    Equal(NativeWindowFallbackReason.None, decision.BackdropFallbackReason);
    Equal(NativeWindowFallbackReason.None, decision.ChromeFallbackReason);
    True(decision.UsesNativeTitleBar, "The native title bar was not selected.");
}

static void HighContrastSelectsSolidBackdrop()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(
        AvailableInputs() with { IsHighContrast = true });
    Equal(NativeWindowBackdropKind.Solid, decision.Backdrop);
    Equal(NativeWindowFallbackReason.HighContrast, decision.BackdropFallbackReason);
    Equal(NativeWindowChromeKind.AppWindow, decision.Chrome);
}

static void DisabledTransparencySelectsSolidBackdrop()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(
        AvailableInputs() with { IsTransparencyEnabled = false });
    Equal(NativeWindowBackdropKind.Solid, decision.Backdrop);
    Equal(NativeWindowFallbackReason.TransparencyDisabled, decision.BackdropFallbackReason);
}

static void UnsupportedWindowsSelectsSystemChrome()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(
        AvailableInputs() with { IsWindows11OrLater = false });
    Equal(NativeWindowBackdropKind.Solid, decision.Backdrop);
    Equal(NativeWindowChromeKind.System, decision.Chrome);
    Equal(NativeWindowFallbackReason.UnsupportedOperatingSystem, decision.BackdropFallbackReason);
    Equal(NativeWindowFallbackReason.UnsupportedOperatingSystem, decision.ChromeFallbackReason);
}

static void MissingAppWindowSelectsSystemChrome()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(
        AvailableInputs() with { IsAppWindowAvailable = false });
    Equal(NativeWindowChromeKind.System, decision.Chrome);
    Equal(NativeWindowFallbackReason.AppWindowUnavailable, decision.ChromeFallbackReason);
}

static void MissingTitleBarCustomizationSelectsSystemChrome()
{
    NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(
        AvailableInputs() with { IsTitleBarCustomizationAvailable = false });
    Equal(NativeWindowChromeKind.System, decision.Chrome);
    Equal(NativeWindowFallbackReason.TitleBarCustomizationUnavailable, decision.ChromeFallbackReason);
}

static void DragGeometryConvertsDips()
{
    True(
        NativeWindowPolicy.TryCreateDragRectangle(12.25, 4.5, 360.25, 38.5, 1.5, out NativePixelRect rectangle),
        "Valid drag geometry was rejected.");
    Equal(new NativePixelRect(18, 7, 540, 58), rectangle);
}

static void InvalidDragGeometryFailsClosed()
{
    True(!NativeWindowPolicy.TryCreateDragRectangle(-1, 0, 100, 40, 1, out _), "A negative origin was accepted.");
    True(!NativeWindowPolicy.TryCreateDragRectangle(0, 0, 0, 40, 1, out _), "A zero width was accepted.");
    True(!NativeWindowPolicy.TryCreateDragRectangle(0, 0, 100, 40, 0, out _), "A zero scale was accepted.");
    True(!NativeWindowPolicy.TryCreateDragRectangle(double.NaN, 0, 100, 40, 1, out _), "NaN was accepted.");
    True(!NativeWindowPolicy.TryCreateDragRectangle(0, 0, 100, double.PositiveInfinity, 1, out _), "Infinity was accepted.");
}

static void OversizedDragGeometryFailsClosed()
{
    True(
        !NativeWindowPolicy.TryCreateDragRectangle(0, 0, 800_001, 40, 2, out _),
        "An oversized drag region was accepted.");
}

static void ZeroWindowHandleFailsClosed()
{
    NativeWindowAttachment attachment = WindowsNativeWindowSession.TryAttach(
        nint.Zero,
        AvailableInputs(),
        useDarkCaption: true,
        out WindowsNativeWindowSession? session);
    True(session is null, "A native session was retained for a zero handle.");
    True(!attachment.AppWindowAttached, "AppWindow was reported attached for a zero handle.");
    True(!attachment.BackdropApplied, "A backdrop was reported applied for a zero handle.");
    Equal(NativeWindowChromeKind.System, attachment.Decision.Chrome);
    Equal(NativeWindowBackdropKind.Solid, attachment.Decision.Backdrop);
    Equal(NativeWindowFallbackReason.NativeCallFailed, attachment.Decision.ChromeFallbackReason);
}

static void NullCapabilityInputsAreRejected()
{
    Throws<ArgumentNullException>(() => NativeWindowPolicy.Evaluate(null!));
    Throws<ArgumentNullException>(() =>
        WindowsNativeWindowSession.TryAttach(nint.Zero, null!, false, out _));
}

static NativeWindowCapabilityInputs AvailableInputs() => new(
    IsWindows11OrLater: true,
    IsHighContrast: false,
    IsTransparencyEnabled: true,
    IsAppWindowAvailable: true,
    IsTitleBarCustomizationAvailable: true);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
}

static void True(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
