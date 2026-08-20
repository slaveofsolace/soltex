using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Soltex.Whisper.Windows.Tests;

internal sealed class LiveElevatedTargetHost : IAsyncDisposable
{
    private const uint CloseWindowMessage = 0x0010;
    private const int RestoreWindow = 9;
    private const string MarkerPrefix = "SoltexElevatedTarget";
    private readonly Process _process;
    private nint _window;

    private LiveElevatedTargetHost(Process process)
    {
        _process = process;
    }

    internal int ProcessId => _process.Id;

    internal static async ValueTask<LiveElevatedTargetHost> CreateAsync()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string? configuredDotnet =
            Environment.GetEnvironmentVariable("SOLTEX_WHISPER_DOTNET_PATH");
        if (string.IsNullOrWhiteSpace(configuredDotnet))
        {
            throw new InvalidOperationException(
                "SOLTEX_WHISPER_DOTNET_PATH must name the owner-selected .NET host.");
        }

        string dotnetPath = Path.GetFullPath(configuredDotnet);
        if (!File.Exists(dotnetPath) ||
            !string.Equals(Path.GetFileName(dotnetPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(dotnetPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "The owner-selected .NET host is unavailable or unsafe.");
        }

        string marker = MarkerPrefix + Guid.NewGuid().ToString("N");
        ProcessStartInfo start = new(dotnetPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(assemblyPath) ??
                throw new InvalidOperationException("The test assembly has no parent directory.")
        };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("--elevated-target-host");
        start.ArgumentList.Add(marker);

        Process process;
        try
        {
            process = Process.Start(start) ??
                throw new InvalidOperationException("The elevated target process did not start.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(
                "The owner declined the elevated target proof prompt.",
                exception);
        }

        LiveElevatedTargetHost host = new(process);
        try
        {
            await host.WaitForWindowAsync(marker);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal static int RunChild(string marker)
    {
        if (marker.Length != MarkerPrefix.Length + 32 ||
            !marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) ||
            marker[MarkerPrefix.Length..].Any(character => !char.IsAsciiHexDigit(character)))
        {
            return 2;
        }

        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using System.Windows.Forms.Form form = new()
                {
                    Text = marker,
                    Width = 520,
                    Height = 180,
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    TopMost = true
                };
                using System.Windows.Forms.TextBox text = new()
                {
                    Text = "controlled elevated fixture",
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Multiline = true
                };
                form.Controls.Add(text);
                form.Shown += (_, _) => text.Focus();
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = false,
            Name = "Soltex Whisper elevated target"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure is null ? 0 : 1;
    }

    internal async Task FocusEditorAsync()
    {
        if (_window == 0 || !IsWindow(_window))
        {
            throw new InvalidOperationException("The controlled elevated target is unavailable.");
        }

        _ = ShowWindow(_window, RestoreWindow);
        _ = SetForegroundWindow(_window);
        await Task.Delay(250);
        AutomationElement root = AutomationElement.FromHandle(_window);
        AutomationElement? editor = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));
        (editor ?? root).SetFocus();
        await Task.Delay(250);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited && _window != 0 && IsWindow(_window))
            {
                _ = GetWindowThreadProcessId(_window, out uint processId);
                if (processId != _process.Id)
                {
                    throw new InvalidOperationException(
                        "Refusing to close the elevated target after window ownership drift.");
                }

                _ = PostMessage(_window, CloseWindowMessage, 0, 0);
            }

            if (!_process.HasExited)
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(timeout.Token);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task WaitForWindowAsync(string marker)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The elevated target exited with code {_process.ExitCode}.");
            }

            _process.Refresh();
            nint window = _process.MainWindowHandle;
            if (window != 0)
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (string.Equals(root.Current.Name, marker, StringComparison.Ordinal))
                {
                    _window = window;
                    return;
                }
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            "The controlled elevated target did not become ready after owner approval.");
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

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
