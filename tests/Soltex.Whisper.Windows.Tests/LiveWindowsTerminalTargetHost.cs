using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Soltex.Whisper.Windows.Tests;

internal sealed class LiveWindowsTerminalTargetHost : IAsyncDisposable
{
    private const uint CloseWindowMessage = 0x0010;
    private const int RestoreWindow = 9;
    private readonly nint _window;
    private readonly int _windowProcessId;

    private LiveWindowsTerminalTargetHost(nint window, int windowProcessId)
    {
        _window = window;
        _windowProcessId = windowProcessId;
    }

    internal static async ValueTask<LiveWindowsTerminalTargetHost> CreateAsync()
    {
        string windowMarker = "Soltex Whisper Terminal Matrix " +
            Guid.NewGuid().ToString("N");
        string terminalPath = ResolveTerminalPath();
        ProcessStartInfo start = new(terminalPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };
        start.ArgumentList.Add("-w");
        start.ArgumentList.Add("new");
        start.ArgumentList.Add("new-tab");
        start.ArgumentList.Add("--title");
        start.ArgumentList.Add(windowMarker);
        start.ArgumentList.Add("--suppressApplicationTitle");
        start.ArgumentList.Add("cmd.exe");
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/q");
        start.ArgumentList.Add("/k");
        start.ArgumentList.Add("prompt SOLTEX$G");

        using Process launcher = Process.Start(start) ??
            throw new InvalidOperationException("The controlled Windows Terminal launcher did not start.");
        nint window = await WaitForWindowAsync(windowMarker);
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId > int.MaxValue)
        {
            _ = PostMessage(window, CloseWindowMessage, 0, 0);
            throw new InvalidOperationException(
                "The controlled Windows Terminal window had no usable process identity.");
        }

        using Process windowProcess = Process.GetProcessById(checked((int)processId));
        if (!string.Equals(
                windowProcess.ProcessName,
                "WindowsTerminal",
                StringComparison.OrdinalIgnoreCase))
        {
            _ = PostMessage(window, CloseWindowMessage, 0, 0);
            throw new InvalidOperationException(
                "The uniquely titled target was not owned by Windows Terminal.");
        }

        return new LiveWindowsTerminalTargetHost(window, checked((int)processId));
    }

    internal async Task FocusTerminalAsync()
    {
        if (!IsWindow(_window))
        {
            throw new InvalidOperationException("The controlled Windows Terminal window is unavailable.");
        }

        _ = ShowWindow(_window, RestoreWindow);
        _ = SetForegroundWindow(_window);
        await Task.Delay(250);
        AutomationElement root = AutomationElement.FromHandle(_window);
        AutomationElement? terminal = root.FindFirst(
            TreeScope.Descendants,
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Document),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Edit)));
        (terminal ?? root).SetFocus();
        await Task.Delay(250);
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsWindow(_window))
        {
            return;
        }

        _ = GetWindowThreadProcessId(_window, out uint currentProcessId);
        if (currentProcessId != _windowProcessId)
        {
            throw new InvalidOperationException(
                "Refusing to close a Windows Terminal window after ownership drift.");
        }

        _ = PostMessage(_window, CloseWindowMessage, 0, 0);
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!IsWindow(_window))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            "The controlled Windows Terminal window did not close within its bound.");
    }

    private static string ResolveTerminalPath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "wt.exe");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Windows Terminal is unavailable on this owner host.");
        }

        return path;
    }

    private static async Task<nint> WaitForWindowAsync(string marker)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            nint found = 0;
            _ = EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window))
                {
                    return true;
                }

                int length = GetWindowTextLength(window);
                if (length <= 0 || length > 512)
                {
                    return true;
                }

                char[] title = new char[length + 1];
                int copied = GetWindowText(window, title, title.Length);
                if (copied > 0 &&
                    new string(title, 0, copied).Contains(marker, StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }

                return true;
            }, 0);
            if (found != 0)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            "The controlled Windows Terminal window did not become ready.");
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        nint window,
        [Out] char[] text,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
