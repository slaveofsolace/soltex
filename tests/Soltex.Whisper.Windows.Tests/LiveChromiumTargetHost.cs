using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Soltex.Whisper.Windows.Tests;

internal sealed class LiveChromiumTargetHost : IAsyncDisposable
{
    internal const string InputProbeText = "soltex browser input matrix probe";
    internal const string ContentEditableProbeText = "soltex browser contenteditable matrix probe";

    private const int RestoreWindow = 9;
    private const string OwnedDirectoryPrefix = "soltex-whisper-chromium-";
    private readonly string _ownedRoot;
    private readonly Process _process;

    private LiveChromiumTargetHost(string ownedRoot, Process process)
    {
        _ownedRoot = ownedRoot;
        _process = process;
    }

    internal static async ValueTask<LiveChromiumTargetHost> CreateAsync()
    {
        string browserPath = ResolveBrowserPath();
        string ownedRoot = Path.Combine(
            Path.GetTempPath(),
            OwnedDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ownedRoot);
        string fixturePath = Path.Combine(ownedRoot, "target.html");
        await File.WriteAllTextAsync(
            fixturePath,
            CreateFixture(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ProcessStartInfo start = new(browserPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = ownedRoot
        };
        start.ArgumentList.Add($"--user-data-dir={Path.Combine(ownedRoot, "profile")}");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        start.ArgumentList.Add("--disable-extensions");
        start.ArgumentList.Add("--disable-sync");
        start.ArgumentList.Add("--disable-background-networking");
        start.ArgumentList.Add("--disable-default-apps");
        start.ArgumentList.Add("--disable-component-update");
        start.ArgumentList.Add("--disable-features=OptimizationHints,MediaRouter");
        start.ArgumentList.Add($"--app={new Uri(fixturePath).AbsoluteUri}");

        Process process = Process.Start(start) ??
            throw new InvalidOperationException("The controlled Chromium process did not start.");
        LiveChromiumTargetHost host = new(ownedRoot, process);
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
            throw new InvalidOperationException("The controlled Chromium window is unavailable.");
        }

        _ = ShowWindow(window, RestoreWindow);
        _ = SetForegroundWindow(window);
        await Task.Delay(500);
    }

    internal async Task WaitForEnterCountAsync(int expectedCount)
    {
        string marker = $"Soltex Chromium enter-{expectedCount}";
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            ThrowIfExited();
            _process.Refresh();
            if (_process.MainWindowTitle.Contains(marker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            "The controlled Chromium target did not observe exactly one Enter dispatch.");
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

        throw new InvalidOperationException("The controlled Chromium window did not become ready.");
    }

    private void ThrowIfExited()
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException(
                $"The controlled Chromium process exited with code {_process.ExitCode}.");
        }
    }

    private async Task DeleteOwnedRootAsync()
    {
        string fullRoot = Path.GetFullPath(_ownedRoot);
        string tempRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(
                Path.GetDirectoryName(fullRoot),
                tempRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullRoot).StartsWith(OwnedDirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unrecognized Chromium harness path.");
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

    private static string ResolveBrowserPath()
    {
        string? configured = Environment.GetEnvironmentVariable("SOLTEX_WHISPER_CHROMIUM_PATH");
        string[] candidates =
        [
            configured ?? string.Empty,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe")
        ];
        string? path = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(Path.GetFullPath)
            .FirstOrDefault(candidate =>
                File.Exists(candidate) &&
                (string.Equals(
                    Path.GetFileName(candidate),
                    "chrome.exe",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    Path.GetFileName(candidate),
                    "msedge.exe",
                    StringComparison.OrdinalIgnoreCase)));
        return path ?? throw new InvalidOperationException(
            "No owner-selected Chromium executable is available for the opt-in matrix.");
    }

    private static string CreateFixture() => """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Soltex Chromium target matrix</title>
        </head>
        <body>
          <label for="probe">Controlled dictation target</label>
          <input id="probe" aria-label="Controlled dictation target" autofocus>
          <div id="editable" role="textbox" aria-label="Controlled contenteditable target"
               aria-multiline="true" contenteditable="true" tabindex="0"></div>
          <script>
            const probe = document.getElementById('probe');
            const editable = document.getElementById('editable');
            let enters = 0;
            probe.addEventListener('keydown', event => {
              if (event.key === 'Enter') {
                event.preventDefault();
                enters += 1;
                document.title = `Soltex Chromium enter-${enters}`;
                editable.focus();
                const selection = window.getSelection();
                const range = document.createRange();
                range.selectNodeContents(editable);
                selection.removeAllRanges();
                selection.addRange(range);
              }
            });
            editable.addEventListener('keydown', event => {
              if (event.key === 'Enter') {
                event.preventDefault();
                enters += 1;
                document.title = `Soltex Chromium enter-${enters}`;
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
