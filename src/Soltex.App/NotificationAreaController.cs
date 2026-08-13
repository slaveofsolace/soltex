using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Soltex.App;

internal sealed class NotificationAreaController : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = 0x8000 + 0x51;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetFocus = 0x00000003;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint NotifyIconInfo = 0x00000010;
    private const uint NotifyIconShowTip = 0x00000080;
    private const uint NotifyIconInfoFlag = 0x00000001;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackReturnCommand = 0x0100;
    private const uint OpenCommand = 1;
    private const uint ExitCommand = 2;
    private const int DefaultApplicationIcon = 32_512;
    private const int WindowNull = 0x0000;
    private const int WindowContextMenu = 0x007B;
    private const int WindowLeftButtonDoubleClick = 0x0203;
    private const int WindowRightButtonUp = 0x0205;
    private const int NotifySelect = 0x0400;
    private const int NotifyKeySelect = 0x0401;

    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly nint _iconHandle;
    private bool _desiredVisible;
    private bool _iconAdded;
    private bool _backgroundNoticeShown;
    private bool _available = true;
    private bool _disposed;

    internal NotificationAreaController(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A valid owned window handle is required for notification-area messages.",
                nameof(windowHandle));
        }

        if (TaskbarCreatedMessage == 0)
        {
            throw new Win32Exception(
                "Windows could not register the notification-area recovery message.");
        }

        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException(
                "The Soltex window message source is unavailable.");
        nint iconHandle = ExtractProcessIcon();
        try
        {
            _source.AddHook(WindowHook);
        }
        catch
        {
            _ = DestroyIcon(iconHandle);
            throw;
        }

        _iconHandle = iconHandle;
    }

    internal bool IsAvailable => !_disposed && _available;

    internal bool IsVisible => IsAvailable && _iconAdded;

    internal event EventHandler? OpenRequested;

    internal event EventHandler? ExitRequested;

    internal event EventHandler? AvailabilityChanged;

    internal void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _desiredVisible = visible;
        if (!visible)
        {
            RemoveIcon();
            return;
        }

        if (!_available || _iconAdded)
        {
            return;
        }

        AddIcon();
    }

    internal void ShowBackgroundNotice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_backgroundNoticeShown || !_iconAdded)
        {
            return;
        }

        NotifyIconData data = CreateData(NotifyIconInfo);
        data.InfoTitle = "Soltex is still running";
        data.Info =
            "Performance sampling is paused while the window is hidden. Open or exit Soltex from the notification area.";
        data.InfoFlags = NotifyIconInfoFlag;
        if (!Shell_NotifyIconW(NotifyIconModify, ref data))
        {
            MarkUnavailable();
            return;
        }

        _backgroundNoticeShown = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _desiredVisible = false;
        RemoveIcon();
        if (!_source.IsDisposed)
        {
            _source.RemoveHook(WindowHook);
        }

        _ = DestroyIcon(_iconHandle);
    }

    private nint WindowHook(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        _ = windowHandle;
        _ = wParam;
        if (_disposed)
        {
            return nint.Zero;
        }

        if (TaskbarCreatedMessage != 0 && message == TaskbarCreatedMessage)
        {
            _iconAdded = false;
            if (_desiredVisible && _available)
            {
                AddIcon();
            }

            return nint.Zero;
        }

        if (message != CallbackMessage)
        {
            return nint.Zero;
        }

        int notification = unchecked((int)(long)lParam) & 0xffff;
        switch (notification)
        {
            case WindowLeftButtonDoubleClick:
            case NotifySelect:
            case NotifyKeySelect:
                handled = true;
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case WindowContextMenu:
            case WindowRightButtonUp:
                handled = true;
                ShowContextMenu();
                break;
        }

        return nint.Zero;
    }

    private void AddIcon()
    {
        NotifyIconData data = CreateData(
            NotifyIconMessage | NotifyIconIcon | NotifyIconTip | NotifyIconShowTip);
        data.CallbackMessage = CallbackMessage;
        data.IconHandle = _iconHandle;
        data.Tip = "Soltex system workspace";
        if (!Shell_NotifyIconW(NotifyIconAdd, ref data))
        {
            MarkUnavailable();
            return;
        }

        _iconAdded = true;
        data = CreateData(0);
        data.TimeoutOrVersion = NotifyIconVersion4;
        if (!Shell_NotifyIconW(NotifyIconSetVersion, ref data))
        {
            RemoveIcon();
            MarkUnavailable();
        }
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        NotifyIconData data = CreateData(0);
        _ = Shell_NotifyIconW(NotifyIconDelete, ref data);
        _iconAdded = false;
    }

    private void ShowContextMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            if (!AppendMenuW(menu, MenuString, OpenCommand, "Open Soltex") ||
                !AppendMenuW(menu, MenuSeparator, 0, null) ||
                !AppendMenuW(menu, MenuString, ExitCommand, "Exit Soltex") ||
                !GetCursorPos(out Point cursor))
            {
                return;
            }

            _ = SetForegroundWindow(_windowHandle);
            uint command = TrackPopupMenuEx(
                menu,
                TrackRightButton | TrackReturnCommand,
                cursor.X,
                cursor.Y,
                _windowHandle,
                nint.Zero);
            NotifyIconData focusData = CreateData(0);
            _ = Shell_NotifyIconW(NotifyIconSetFocus, ref focusData);
            _ = PostMessageW(_windowHandle, WindowNull, nint.Zero, nint.Zero);
            if (command == OpenCommand)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (command == ExitCommand)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void MarkUnavailable()
    {
        if (!_available)
        {
            return;
        }

        RemoveIcon();
        _available = false;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private NotifyIconData CreateData(uint flags) =>
        new()
        {
            Size = checked((uint)Marshal.SizeOf<NotifyIconData>()),
            WindowHandle = _windowHandle,
            IconId = IconId,
            Flags = flags,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty
        };

    private static nint ExtractProcessIcon()
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException(
                "The Soltex executable path is unavailable for icon extraction.");
        }

        uint extracted = ExtractIconExW(
            processPath,
            0,
            out nint largeIcon,
            out nint smallIcon,
            1);
        nint selected = smallIcon != nint.Zero ? smallIcon : largeIcon;
        nint unused = selected == smallIcon ? largeIcon : smallIcon;
        if (unused != nint.Zero)
        {
            _ = DestroyIcon(unused);
        }

        if (extracted == 0 || selected == nint.Zero)
        {
            if (selected != nint.Zero)
            {
                _ = DestroyIcon(selected);
            }

            nint sharedIcon = LoadIconW(nint.Zero, new nint(DefaultApplicationIcon));
            nint copiedIcon = sharedIcon == nint.Zero
                ? nint.Zero
                : CopyIcon(sharedIcon);
            if (copiedIcon == nint.Zero)
            {
                throw new Win32Exception(
                    "Windows could not load a notification-area icon.");
            }

            return copiedIcon;
        }

        return selected;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(
        uint message,
        ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(
        string file,
        int iconIndex,
        out nint largeIcon,
        out nint smallIcon,
        uint iconCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        nint menu,
        uint flags,
        nuint newItem,
        string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint window,
        nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        int message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal nint WindowHandle;
        internal uint IconId;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid ItemGuid;
        internal nint BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        internal readonly int X;
        internal readonly int Y;
    }
}
