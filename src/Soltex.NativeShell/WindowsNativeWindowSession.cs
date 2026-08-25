using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Soltex.NativeShell;

public sealed record NativeWindowAttachment(
    NativeWindowPolicyDecision Decision,
    bool AppWindowAttached,
    bool BackdropApplied,
    int CaptionLeftInset,
    int CaptionRightInset);

public sealed partial class WindowsNativeWindowSession : IDisposable
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMainWindow = 2;

    private readonly nint _windowHandle;
    private AppWindow? _appWindow;
    private bool _disposed;

    private WindowsNativeWindowSession(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public static NativeWindowCapabilityInputs ProbeCapabilities(
        bool isHighContrast,
        bool isTransparencyEnabled)
    {
        bool windows11OrLater = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        bool appWindowAvailable = false;
        bool titleBarCustomizationAvailable = false;

        if (windows11OrLater)
        {
            try
            {
                titleBarCustomizationAvailable = AppWindowTitleBar.IsCustomizationSupported();
                appWindowAvailable = true;
            }
            catch (Exception exception) when (IsExpectedNativeFailure(exception))
            {
                appWindowAvailable = false;
                titleBarCustomizationAvailable = false;
            }
        }

        return new NativeWindowCapabilityInputs(
            windows11OrLater,
            isHighContrast,
            isTransparencyEnabled,
            appWindowAvailable,
            titleBarCustomizationAvailable);
    }

    public static NativeWindowAttachment TryAttach(
        nint windowHandle,
        NativeWindowCapabilityInputs inputs,
        bool useDarkCaption,
        out WindowsNativeWindowSession? session)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        session = null;

        NativeWindowPolicyDecision decision = NativeWindowPolicy.Evaluate(inputs);
        if (windowHandle == nint.Zero)
        {
            return WithNativeFailure(decision);
        }

        WindowsNativeWindowSession candidate = new(windowHandle);
        bool appWindowAttached = candidate.TryAttachAppWindow(decision);
        bool backdropApplied = candidate.TryApplyBackdrop(decision.Backdrop, useDarkCaption);

        if (!appWindowAttached && decision.Chrome == NativeWindowChromeKind.AppWindow)
        {
            decision = decision with
            {
                Chrome = NativeWindowChromeKind.System,
                ChromeFallbackReason = NativeWindowFallbackReason.NativeCallFailed
            };
        }

        if (!backdropApplied && decision.Backdrop == NativeWindowBackdropKind.Mica)
        {
            decision = decision with
            {
                Backdrop = NativeWindowBackdropKind.Solid,
                BackdropFallbackReason = NativeWindowFallbackReason.NativeCallFailed
            };
        }

        session = candidate;
        int leftInset = candidate._appWindow?.TitleBar?.LeftInset ?? 0;
        int rightInset = candidate._appWindow?.TitleBar?.RightInset ?? 0;
        return new NativeWindowAttachment(
            decision,
            appWindowAttached,
            backdropApplied,
            leftInset,
            rightInset);
    }

    public bool TrySetDragRectangles(IReadOnlyList<NativePixelRect> rectangles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rectangles);

        if (_appWindow?.TitleBar is not AppWindowTitleBar titleBar || rectangles.Count == 0)
        {
            return false;
        }

        RectInt32[] nativeRectangles = new RectInt32[rectangles.Count];
        for (int index = 0; index < rectangles.Count; index++)
        {
            NativePixelRect rectangle = rectangles[index];
            if (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                return false;
            }

            nativeRectangles[index] = new RectInt32(
                rectangle.X,
                rectangle.Y,
                rectangle.Width,
                rectangle.Height);
        }

        try
        {
            titleBar.SetDragRectangles(nativeRectangles);
            return true;
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            return false;
        }
    }

    public bool TryUpdateBackdrop(NativeWindowBackdropKind backdrop, bool useDarkCaption)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return TryApplyBackdrop(backdrop, useDarkCaption);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appWindow = null;
    }

    private static NativeWindowAttachment WithNativeFailure(NativeWindowPolicyDecision decision)
    {
        NativeWindowPolicyDecision failed = decision with
        {
            Backdrop = NativeWindowBackdropKind.Solid,
            Chrome = NativeWindowChromeKind.System,
            BackdropFallbackReason = NativeWindowFallbackReason.NativeCallFailed,
            ChromeFallbackReason = NativeWindowFallbackReason.NativeCallFailed
        };
        return new NativeWindowAttachment(failed, false, false, 0, 0);
    }

    private bool TryAttachAppWindow(NativeWindowPolicyDecision decision)
    {
        if (decision.Chrome != NativeWindowChromeKind.AppWindow)
        {
            return false;
        }

        try
        {
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow?.TitleBar is not AppWindowTitleBar titleBar ||
                !AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow = null;
                return false;
            }

            titleBar.ExtendsContentIntoTitleBar = true;
            return true;
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            _appWindow = null;
            return false;
        }
    }

    private bool TryApplyBackdrop(NativeWindowBackdropKind backdrop, bool useDarkCaption)
    {
        int darkCaption = useDarkCaption ? 1 : 0;
        int backdropValue = backdrop == NativeWindowBackdropKind.Mica
            ? DwmBackdropMainWindow
            : DwmBackdropNone;

        int captionResult = NativeMethods.DwmSetWindowAttribute(
            _windowHandle,
            DwmUseImmersiveDarkMode,
            ref darkCaption,
            sizeof(int));
        int backdropResult = NativeMethods.DwmSetWindowAttribute(
            _windowHandle,
            DwmSystemBackdropType,
            ref backdropValue,
            sizeof(int));

        return captionResult >= 0 && backdropResult >= 0;
    }

    private static bool IsExpectedNativeFailure(Exception exception) =>
        exception is COMException or
            Win32Exception or
            ArgumentException or
            InvalidOperationException or
            PlatformNotSupportedException or
            TypeInitializationException or
            TypeLoadException or
            DllNotFoundException or
            FileLoadException or
            BadImageFormatException;

    private static partial class NativeMethods
    {
        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmSetWindowAttribute(
            nint windowHandle,
            int attribute,
            ref int value,
            int valueSize);
    }
}
