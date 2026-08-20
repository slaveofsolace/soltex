using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace Soltex.Whisper.Windows.Tests;

internal sealed class LiveWinUiTargetHost : IAsyncDisposable
{
    internal const string ProbeText = "soltex winui matrix probe";

    private const string AutomationId = "SoltexWhisperWinUiTarget";
    private const string EnterReceiptAutomationId = "SoltexWhisperWinUiEnterReceipt";
    private const string OneEnterReceipt = "Enter receipts: 1";
    private const int RestoreWindow = 9;
    private readonly string _executablePath;
    private readonly Process _process;

    private LiveWinUiTargetHost(string executablePath, Process process)
    {
        _executablePath = executablePath;
        _process = process;
    }

    internal int ProcessId => _process.Id;

    internal string FrameworkId { get; private set; } = string.Empty;

    internal static async ValueTask<LiveWinUiTargetHost> CreateAsync()
    {
        string executablePath = ResolveTargetPath();
        EnsureTargetIsNotRunning(executablePath);

        ProcessStartInfo start = new(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ??
                throw new InvalidOperationException("The controlled WinUI target had no directory.")
        };
        start.Environment["DOTNET_ROOT"] = ResolveDotnetRoot();
        Process process = Process.Start(start) ??
            throw new InvalidOperationException("The controlled WinUI target did not start.");
        LiveWinUiTargetHost host = new(executablePath, process);
        try
        {
            await host.WaitForWindowAsync();
            await host.FocusEditorAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal async Task FocusEditorAsync()
    {
        await WaitForWindowAsync();
        VerifyProcessIdentity();
        _process.Refresh();
        nint window = _process.MainWindowHandle;
        if (window == 0)
        {
            throw new InvalidOperationException("The controlled WinUI window is unavailable.");
        }

        AutomationElement root = AutomationElement.FromHandle(window);
        AutomationElement? editor = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                AutomationId));
        if (editor is null ||
            editor.Current.ControlType != ControlType.Edit ||
            !editor.Current.IsEnabled ||
            !editor.Current.IsKeyboardFocusable ||
            editor.Current.IsPassword ||
            editor.Current.ProcessId != _process.Id ||
            !editor.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern) ||
            valuePattern is not ValuePattern value ||
            value.Current.IsReadOnly)
        {
            throw new InvalidOperationException(
                "The controlled WinUI target did not expose an editable ValuePattern.");
        }

        string frameworkId = editor.Current.FrameworkId;
        if (!string.Equals(frameworkId, "XAML", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(frameworkId, "WinUI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The controlled WinUI target did not expose a XAML automation provider.");
        }

        FrameworkId = frameworkId;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (ActivateOwnedWindow(window))
            {
                editor.SetFocus();
                await Task.Delay(50);
                AutomationElement? focused = AutomationElement.FocusedElement;
                if (focused is not null &&
                    focused.Current.ProcessId == _process.Id &&
                    string.Equals(
                        focused.Current.AutomationId,
                        AutomationId,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            "The controlled WinUI target could not acquire verified foreground focus.");
    }

    internal async Task WaitForEnterAsync()
    {
        Stopwatch timeout = Stopwatch.StartNew();
        uint foregroundProcessId = 0;
        int focusedProcessId = 0;
        bool focusedAutomationIdMatches = false;
        int observedLength = -1;
        int observedNewlineCount = -1;
        bool observedProbeMatches = false;
        bool observedOneEnterReceipt = false;
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            ThrowIfExited();
            _process.Refresh();
            nint foreground = GetForegroundWindow();
            if (foreground != 0)
            {
                _ = GetWindowThreadProcessId(foreground, out foregroundProcessId);
            }

            AutomationElement? focused = AutomationElement.FocusedElement;
            focusedProcessId = focused?.Current.ProcessId ?? 0;
            focusedAutomationIdMatches = string.Equals(
                focused?.Current.AutomationId,
                AutomationId,
                StringComparison.Ordinal);
            nint window = _process.MainWindowHandle;
            if (window != 0)
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                AutomationElement? receipt = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        EnterReceiptAutomationId));
                observedOneEnterReceipt = string.Equals(
                    receipt?.Current.Name,
                    OneEnterReceipt,
                    StringComparison.Ordinal);
                if (observedOneEnterReceipt)
                {
                    return;
                }

                AutomationElement? editor = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        AutomationId));
                if (editor is not null &&
                    editor.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern) &&
                    valuePattern is ValuePattern value)
                {
                    string observed = value.Current.Value;
                    observedLength = observed.Length;
                    if (observed.Length <= 256)
                    {
                        string normalized = observed.Replace(
                            "\r",
                            string.Empty,
                            StringComparison.Ordinal);
                        observedNewlineCount = normalized.Count(character => character == '\n');
                        observedProbeMatches = string.Equals(
                            normalized.Replace("\n", string.Empty, StringComparison.Ordinal),
                            ProbeText,
                            StringComparison.Ordinal);
                    }
                }
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            $"The controlled WinUI target did not receive exactly one Enter key-down " +
            $"(target_pid={_process.Id}; foreground_pid={foregroundProcessId}; " +
            $"focused_pid={focusedProcessId}; focused_target={focusedAutomationIdMatches}; " +
            $"enter_receipt={observedOneEnterReceipt}; " +
            $"value_length={observedLength}; newlines={observedNewlineCount}; " +
            $"probe_matches={observedProbeMatches}).");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process.HasExited)
            {
                return;
            }

            VerifyProcessIdentity();
            _ = _process.CloseMainWindow();
            using CancellationTokenSource gracefulTimeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(gracefulTimeout.Token);
                return;
            }
            catch (OperationCanceledException)
            {
            }

            VerifyProcessIdentity();
            _process.Kill(entireProcessTree: true);
            using CancellationTokenSource killTimeout = new(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(killTimeout.Token);
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task WaitForWindowAsync()
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            ThrowIfExited();
            _process.Refresh();
            if (_process.MainWindowHandle != 0)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("The controlled WinUI window did not become ready.");
    }

    private void ThrowIfExited()
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException(
                $"The controlled WinUI target exited with code {_process.ExitCode}.");
        }
    }

    private void VerifyProcessIdentity()
    {
        _process.Refresh();
        string? currentPath = _process.MainModule?.FileName;
        if (!string.Equals(
                currentPath,
                _executablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The controlled WinUI process changed executable identity.");
        }
    }

    private static string ResolveTargetPath()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "SOLTEX_WHISPER_WINUI_TARGET_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "SOLTEX_WHISPER_WINUI_TARGET_PATH must name the owner-built WinUI target.");
        }

        string path = Path.GetFullPath(configured);
        if (!File.Exists(path) ||
            !string.Equals(
                Path.GetFileName(path),
                "Soltex.Whisper.WinUiTarget.exe",
                StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "The owner-selected WinUI target is unavailable or unsafe.");
        }

        return path;
    }

    private static string ResolveDotnetRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("SOLTEX_WHISPER_DOTNET_ROOT");
        string? processRoot = Path.GetDirectoryName(Environment.ProcessPath);
        string? candidate = string.IsNullOrWhiteSpace(configured) ? processRoot : configured;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("The controlled .NET root is unavailable.");
        }

        string root = Path.GetFullPath(candidate);
        string dotnetPath = Path.Combine(root, "dotnet.exe");
        if (!File.Exists(dotnetPath) ||
            (File.GetAttributes(dotnetPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The controlled .NET root is unavailable or unsafe.");
        }

        return root;
    }

    private static void EnsureTargetIsNotRunning(string executablePath)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        Process[] candidates = Process.GetProcessesByName(processName);
        try
        {
            foreach (Process process in candidates)
            {
                try
                {
                    if (string.Equals(
                            process.MainModule?.FileName,
                            executablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The owner-built WinUI target is already running.");
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }
        }
        finally
        {
            foreach (Process process in candidates)
            {
                process.Dispose();
            }
        }
    }

    private static bool ActivateOwnedWindow(nint window)
    {
        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(window, out _);
        nint foregroundWindow = GetForegroundWindow();
        uint foregroundThread = foregroundWindow == 0
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        bool targetAttached = false;
        bool foregroundAttached = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                targetAttached = AttachThreadInput(currentThread, targetThread, attach: true);
            }

            if (foregroundThread != 0 &&
                foregroundThread != currentThread &&
                foregroundThread != targetThread)
            {
                foregroundAttached = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: true);
            }

            _ = ShowWindow(window, RestoreWindow);
            _ = BringWindowToTop(window);
            return SetForegroundWindow(window) && GetForegroundWindow() == window;
        }
        finally
        {
            if (foregroundAttached)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, attach: false);
            }

            if (targetAttached)
            {
                _ = AttachThreadInput(currentThread, targetThread, attach: false);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint idAttach,
        uint idAttachTo,
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)] bool attach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
