using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Soltex.Whisper.Windows.Tests;

internal sealed class LiveElectronTargetHost : IAsyncDisposable
{
    internal const string ProbeText = "soltex electron matrix probe";

    private const int RestoreWindow = 9;
    private const string OwnedDirectoryPrefix = "soltex-whisper-electron-";
    private readonly string _ownedRoot;
    private readonly Process _process;

    private LiveElectronTargetHost(string ownedRoot, Process process)
    {
        _ownedRoot = ownedRoot;
        _process = process;
    }

    internal static async ValueTask<LiveElectronTargetHost> CreateAsync()
    {
        string electronPath = ResolveElectronPath();
        string ownedRoot = Path.Combine(
            Path.GetTempPath(),
            OwnedDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ownedRoot);
        await File.WriteAllTextAsync(
            Path.Combine(ownedRoot, "package.json"),
            "{\"name\":\"soltex-whisper-electron-target\",\"version\":\"1.0.0\",\"main\":\"main.js\"}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await File.WriteAllTextAsync(
            Path.Combine(ownedRoot, "main.js"),
            CreateMainScript(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await File.WriteAllTextAsync(
            Path.Combine(ownedRoot, "target.html"),
            CreateFixture(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ProcessStartInfo start = new(electronPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = ownedRoot
        };
        start.ArgumentList.Add($"--user-data-dir={Path.Combine(ownedRoot, "profile")}");
        start.ArgumentList.Add("--disable-extensions");
        start.ArgumentList.Add("--disable-background-networking");
        start.ArgumentList.Add("--force-renderer-accessibility");
        start.ArgumentList.Add(ownedRoot);

        Process process = Process.Start(start) ??
            throw new InvalidOperationException("The controlled Electron process did not start.");
        LiveElectronTargetHost host = new(ownedRoot, process);
        try
        {
            await host.WaitForWindowAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal async Task FocusInputAsync()
    {
        await WaitForWindowAsync();
        _process.Refresh();
        nint window = _process.MainWindowHandle;
        if (window == 0)
        {
            throw new InvalidOperationException("The controlled Electron window is unavailable.");
        }

        _ = ShowWindow(window, RestoreWindow);
        _ = SetForegroundWindow(window);
        await Task.Delay(250);
        AutomationElement root = AutomationElement.FromHandle(window);
        AutomationElement? input = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));
        if (input is null)
        {
            throw new InvalidOperationException(
                "The controlled Electron input is unavailable to UI Automation.");
        }

        input.SetFocus();
        await Task.Delay(250);
    }

    internal async Task WaitForEnterAsync()
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            ThrowIfExited();
            _process.Refresh();
            if (_process.MainWindowTitle.Contains("Soltex Electron enter-1", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            "The controlled Electron target did not observe one Enter dispatch.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(timeout.Token);
            }
        }
        finally
        {
            _process.Dispose();
            await DeleteOwnedRootAsync();
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

        throw new InvalidOperationException("The controlled Electron window did not become ready.");
    }

    private void ThrowIfExited()
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException(
                $"The controlled Electron process exited with code {_process.ExitCode}.");
        }
    }

    private async Task DeleteOwnedRootAsync()
    {
        string fullRoot = Path.GetFullPath(_ownedRoot);
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(fullRoot), tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullRoot).StartsWith(OwnedDirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unrecognized Electron harness path.");
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(fullRoot))
                {
                    Directory.Delete(fullRoot, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(100);
            }
        }
    }

    private static string ResolveElectronPath()
    {
        string? configured = Environment.GetEnvironmentVariable("SOLTEX_WHISPER_ELECTRON_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "SOLTEX_WHISPER_ELECTRON_PATH must name an owner-selected electron.exe.");
        }

        string path = Path.GetFullPath(configured);
        if (!File.Exists(path) ||
            !string.Equals(Path.GetFileName(path), "electron.exe", StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "The owner-selected Electron executable is unavailable or unsafe.");
        }

        return path;
    }

    private static string CreateMainScript() => """
        const { app, BrowserWindow, session } = require('electron');
        const path = require('path');

        app.whenReady().then(() => {
          session.defaultSession.webRequest.onBeforeRequest(
            { urls: ['http://*/*', 'https://*/*'] },
            (_details, callback) => callback({ cancel: true }));
          const window = new BrowserWindow({
            width: 720,
            height: 420,
            show: false,
            webPreferences: {
              contextIsolation: true,
              nodeIntegration: false,
              sandbox: true
            }
          });
          window.removeMenu();
          window.loadFile(path.join(__dirname, 'target.html'));
          window.once('ready-to-show', () => {
            window.show();
            window.focus();
            window.webContents.focus();
            window.webContents.executeJavaScript(
              "document.getElementById('probe').focus(); document.getElementById('probe').select();");
          });
        });

        app.on('window-all-closed', () => app.quit());
        """;

    private static string CreateFixture() => """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Soltex Electron target matrix</title>
        </head>
        <body>
          <label for="probe">Controlled Electron dictation target</label>
          <input id="probe" aria-label="Controlled Electron dictation target" autofocus>
          <script>
            const probe = document.getElementById('probe');
            let enters = 0;
            probe.addEventListener('keydown', event => {
              if (event.key === 'Enter') {
                event.preventDefault();
                enters += 1;
                document.title = `Soltex Electron enter-${enters}`;
              }
            });
            window.addEventListener('load', () => {
              probe.focus();
              probe.select();
            });
          </script>
        </body>
        </html>
        """;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
