using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Soltex.Security;

public sealed class PowerShellDefenderClient : IProtectionHealthSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _powerShellPath;
    private readonly string _powerShellDirectory;

    public PowerShellDefenderClient(string? powerShellPath = null)
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string candidate = powerShellPath ?? Path.Combine(
            windowsDirectory,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        _powerShellPath = Path.GetFullPath(candidate);
        _powerShellDirectory = Path.GetDirectoryName(_powerShellPath) ??
            throw new ArgumentException("The Windows PowerShell path has no parent directory.", nameof(powerShellPath));
    }

    public async Task<DefenderHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        WindowsSecurityHealth wscHealth = WindowsSecurityCenter.GetAntivirusHealth();
        ScriptExecution result = await RunScriptAsync(
            UtilityModuleImportScript + Environment.NewLine +
            DefenderModuleImportScript + Environment.NewLine +
            StatusScript,
            environment: null,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return EmptyHealth(wscHealth, result.Message);
        }

        try
        {
            DefenderStatusDto? status = JsonSerializer.Deserialize<DefenderStatusDto>(result.StandardOutput, JsonOptions);
            if (status is null)
            {
                return EmptyHealth(wscHealth, "Defender returned an empty status response.");
            }

            return new DefenderHealthSnapshot(
                DateTimeOffset.UtcNow,
                wscHealth,
                StatusQuerySucceeded: true,
                status.AMRunningMode,
                status.AMServiceEnabled,
                status.AntivirusEnabled,
                status.RealTimeProtectionEnabled,
                status.BehaviorMonitorEnabled,
                status.IoavProtectionEnabled,
                status.NISEnabled,
                status.IsTamperProtected,
                status.CloudProtectionEnabled,
                status.DefenderSignaturesOutOfDate,
                status.AntivirusSignatureVersion,
                status.AntivirusSignatureLastUpdated,
                NormalizeScanAge(status.QuickScanAge),
                NormalizeScanAge(status.FullScanAge),
                Error: null);
        }
        catch (JsonException exception)
        {
            return EmptyHealth(wscHealth, $"Defender status response was invalid: {exception.Message}");
        }
    }

    public async Task<DefenderEventQueryResult> GetRecentEventsAsync(
        TimeSpan lookback,
        int maximumEvents = 32,
        CancellationToken cancellationToken = default)
    {
        if (lookback < TimeSpan.FromMinutes(1) || lookback > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookback),
                "The event lookback must be between one minute and 30 days.");
        }

        if (maximumEvents is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEvents),
                "The event limit must be between 1 and 100.");
        }

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["SOLTEX_EVENT_START_UTC"] = DateTimeOffset.UtcNow.Subtract(lookback).ToString("O"),
            ["SOLTEX_EVENT_MAX"] = maximumEvents.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        ScriptExecution result = await RunScriptAsync(
            UtilityModuleImportScript + Environment.NewLine +
            DiagnosticsModuleImportScript + Environment.NewLine +
            EventScript,
            environment,
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken,
            outputLimit: 512 * 1024).ConfigureAwait(false);
        stopwatch.Stop();

        if (!result.Succeeded)
        {
            return new DefenderEventQueryResult(
                DateTimeOffset.UtcNow,
                Succeeded: false,
                [],
                stopwatch.Elapsed,
                result.Message);
        }

        try
        {
            DefenderEventEnvelope? envelope = JsonSerializer.Deserialize<DefenderEventEnvelope>(
                result.StandardOutput,
                JsonOptions);
            DefenderEventDto[]? returnedItems = envelope?.Items;
            if (returnedItems is { Length: > 0 } && returnedItems.Length > maximumEvents)
            {
                return new DefenderEventQueryResult(
                    DateTimeOffset.UtcNow,
                    Succeeded: false,
                    [],
                    stopwatch.Elapsed,
                    $"Defender returned more than the requested {maximumEvents} event(s).");
            }

            IReadOnlyList<DefenderOperationalEvent> events = returnedItems is null
                ? []
                : returnedItems
                    .Select(item => DefenderEventLogParser.Parse(
                        item.EventId,
                        item.RecordId,
                        item.TimeCreatedUtc,
                        item.Xml))
                    .ToArray();
            return new DefenderEventQueryResult(
                DateTimeOffset.UtcNow,
                Succeeded: true,
                events,
                stopwatch.Elapsed,
                Error: null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new DefenderEventQueryResult(
                DateTimeOffset.UtcNow,
                Succeeded: false,
                [],
                stopwatch.Elapsed,
                $"Defender event response was invalid: {exception.Message}");
        }
    }

    public Task<DefenderCommandResult> RunQuickScanAsync(CancellationToken cancellationToken = default) =>
        RunCommandAsync(
            "Quick scan",
            QuickScanCommand,
            environment: null,
            timeout: TimeSpan.FromHours(1),
            cancellationToken);

    public Task<DefenderCommandResult> RunCustomScanAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested scan target does not exist.", fullPath);
        }

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["SOLTEX_SCAN_PATH"] = fullPath
        };

        return RunCommandAsync(
            "Custom scan",
            CustomScanCommand,
            environment,
            timeout: TimeSpan.FromHours(4),
            cancellationToken);
    }

    public Task<DefenderCommandResult> UpdateSecurityIntelligenceAsync(
        CancellationToken cancellationToken = default) =>
        RunCommandAsync(
            "Security intelligence update",
            UpdateIntelligenceCommand,
            environment: null,
            timeout: TimeSpan.FromMinutes(20),
            cancellationToken);

    private async Task<DefenderCommandResult> RunCommandAsync(
        string operation,
        string command,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string script = UtilityModuleImportScript + Environment.NewLine +
            DefenderModuleImportScript + Environment.NewLine +
            $"$ErrorActionPreference = 'Stop'; {command}; " +
            SuccessOutputCommand;

        Stopwatch stopwatch = Stopwatch.StartNew();
        ScriptExecution result = await RunScriptAsync(
            script,
            environment,
            timeout,
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        string message = result.Succeeded
            ? "Windows Defender completed the requested operation."
            : result.Message;

        return new DefenderCommandResult(
            result.Succeeded,
            operation,
            message,
            result.ExitCode,
            stopwatch.Elapsed);
    }

    private async Task<ScriptExecution> RunScriptAsync(
        string script,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int outputLimit = 8_192)
    {
        if (!File.Exists(_powerShellPath))
        {
            return new ScriptExecution(false, -1, string.Empty, "Windows PowerShell was not found.");
        }

        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        ProcessStartInfo startInfo = new(_powerShellPath)
        {
            Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _powerShellDirectory
        };

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using Process process = new() { StartInfo = startInfo };
        Task<BoundedRead>? stdoutTask = null;
        Task<BoundedRead>? stderrTask = null;
        try
        {
            if (!process.Start())
            {
                return new ScriptExecution(false, -1, string.Empty, "Windows PowerShell could not be started.");
            }

            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            stdoutTask = ReadBoundedUtf8Async(
                process.StandardOutput.BaseStream,
                outputLimit,
                timeoutSource.Token);
            stderrTask = ReadBoundedUtf8Async(
                process.StandardError.BaseStream,
                8_192,
                timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryTerminate(process);
                ObserveCompletion(stdoutTask);
                ObserveCompletion(stderrTask);
                return new ScriptExecution(false, -1, string.Empty, $"The operation exceeded its {timeout:g} time limit.");
            }

            BoundedRead stdout = await stdoutTask.ConfigureAwait(false);
            BoundedRead stderr = await stderrTask.ConfigureAwait(false);
            if (stdout.ExceededLimit)
            {
                return new ScriptExecution(
                    false,
                    process.ExitCode,
                    stdout.Text,
                    $"PowerShell standard output exceeded the {outputLimit:N0}-byte safety limit.");
            }

            if (stderr.ExceededLimit)
            {
                return new ScriptExecution(
                    false,
                    process.ExitCode,
                    stdout.Text,
                    "PowerShell error output exceeded the 8,192-byte safety limit.");
            }

            if (process.ExitCode != 0)
            {
                return new ScriptExecution(
                    false,
                    process.ExitCode,
                    stdout.Text,
                    $"Windows PowerShell reported an error while invoking a supported " +
                    $"Windows security command (exit code {process.ExitCode}).");
            }

            return new ScriptExecution(true, process.ExitCode, stdout.Text.Trim(), string.Empty);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            ObserveCompletion(stdoutTask);
            ObserveCompletion(stderrTask);
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            TryTerminate(process);
            ObserveCompletion(stdoutTask);
            ObserveCompletion(stderrTask);
            return new ScriptExecution(false, -1, string.Empty, exception.Message);
        }
    }

    private static DefenderHealthSnapshot EmptyHealth(WindowsSecurityHealth wscHealth, string error) =>
        new(
            DateTimeOffset.UtcNow,
            wscHealth,
            StatusQuerySucceeded: false,
            AMRunningMode: null,
            AMServiceEnabled: false,
            AntivirusEnabled: false,
            RealTimeProtectionEnabled: false,
            BehaviorMonitorEnabled: false,
            IoavProtectionEnabled: false,
            NetworkInspectionEnabled: false,
            TamperProtected: false,
            CloudProtectionEnabled: false,
            SignaturesOutOfDate: false,
            AntivirusSignatureVersion: null,
            AntivirusSignatureUpdatedAt: null,
            QuickScanAgeDays: null,
            FullScanAgeDays: null,
            Error: error);

    private static uint? NormalizeScanAge(uint? value) => value == uint.MaxValue ? null : value;

    private static async Task<BoundedRead> ReadBoundedUtf8Async(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        byte[] buffer = new byte[Math.Min(maximumBytes, 8_192)];
        using MemoryStream captured = new(capacity: Math.Min(maximumBytes, 8_192));
        bool exceededLimit = false;

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int remaining = maximumBytes - checked((int)captured.Length);
            int keep = Math.Min(read, Math.Max(remaining, 0));
            if (keep > 0)
            {
                captured.Write(buffer, 0, keep);
            }

            exceededLimit |= keep < read;
        }

        string text = Encoding.UTF8.GetString(
            captured.GetBuffer(),
            0,
            checked((int)captured.Length));
        return new BoundedRead(text, exceededLimit);
    }

    private static void ObserveCompletion(Task? task)
    {
        if (task is not null)
        {
            _ = ObserveCompletionAsync(task);
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup observation only: the caller already reports the process failure or cancellation.
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private sealed record ScriptExecution(bool Succeeded, int ExitCode, string StandardOutput, string Message);

    private sealed record BoundedRead(string Text, bool ExceededLimit);

    private sealed class DefenderStatusDto
    {
        public string? AMRunningMode { get; init; }
        public bool AMServiceEnabled { get; init; }
        public bool AntivirusEnabled { get; init; }
        public bool RealTimeProtectionEnabled { get; init; }
        public bool BehaviorMonitorEnabled { get; init; }
        public bool IoavProtectionEnabled { get; init; }
        public bool NISEnabled { get; init; }
        public bool IsTamperProtected { get; init; }
        public bool CloudProtectionEnabled { get; init; }
        public bool DefenderSignaturesOutOfDate { get; init; }
        public string? AntivirusSignatureVersion { get; init; }
        public DateTimeOffset? AntivirusSignatureLastUpdated { get; init; }
        public uint? QuickScanAge { get; init; }
        public uint? FullScanAge { get; init; }
    }

    private sealed class DefenderEventEnvelope
    {
        public DefenderEventDto[]? Items { get; init; }
    }

    private sealed class DefenderEventDto
    {
        public int EventId { get; init; }
        public long RecordId { get; init; }
        public DateTimeOffset TimeCreatedUtc { get; init; }
        public string Xml { get; init; } = string.Empty;
    }

    private const string DefenderModuleImportScript = """
        $defenderModule = [System.IO.Path]::Combine(
            $PSHOME,
            'Modules',
            'Defender',
            'Defender.psd1')
        Import-Module -Name $defenderModule -Force -ErrorAction Stop
        """;

    private const string DiagnosticsModuleImportScript = """
        $diagnosticsModule = [System.IO.Path]::Combine(
            $PSHOME,
            'Modules',
            'Microsoft.PowerShell.Diagnostics',
            'Microsoft.PowerShell.Diagnostics.psd1')
        Import-Module -Name $diagnosticsModule -Force -ErrorAction Stop
        """;

    private const string UtilityModuleImportScript = """
        $utilityModule = [System.IO.Path]::Combine(
            $PSHOME,
            'Modules',
            'Microsoft.PowerShell.Utility',
            'Microsoft.PowerShell.Utility.psd1')
        Import-Module -Name $utilityModule -Force -ErrorAction Stop
        """;

    private const string QuickScanCommand =
        "Defender\\Start-MpScan -ScanType QuickScan -ErrorAction Stop";

    private const string CustomScanCommand =
        "Defender\\Start-MpScan -ScanType CustomScan " +
        "-ScanPath $env:SOLTEX_SCAN_PATH -ErrorAction Stop";

    private const string UpdateIntelligenceCommand =
        "Defender\\Update-MpSignature -ErrorAction Stop";

    private const string SuccessOutputCommand =
        "[ordered]@{ Succeeded = $true; Message = 'Windows Defender accepted the operation.' } | " +
        "Microsoft.PowerShell.Utility\\ConvertTo-Json -Compress";

    private const string StatusScript = """
        $ErrorActionPreference = 'Stop'
        $status = Defender\Get-MpComputerStatus -ErrorAction Stop
        $preferences = Defender\Get-MpPreference -ErrorAction Stop
        [ordered]@{
            AMRunningMode = [string]$status.AMRunningMode
            AMServiceEnabled = [bool]$status.AMServiceEnabled
            AntivirusEnabled = [bool]$status.AntivirusEnabled
            RealTimeProtectionEnabled = [bool]$status.RealTimeProtectionEnabled
            BehaviorMonitorEnabled = [bool]$status.BehaviorMonitorEnabled
            IoavProtectionEnabled = [bool]$status.IoavProtectionEnabled
            NISEnabled = [bool]$status.NISEnabled
            IsTamperProtected = [bool]$status.IsTamperProtected
            CloudProtectionEnabled = ([int]$preferences.MAPSReporting -gt 0)
            DefenderSignaturesOutOfDate = [bool]$status.DefenderSignaturesOutOfDate
            AntivirusSignatureVersion = [string]$status.AntivirusSignatureVersion
            AntivirusSignatureLastUpdated = if ($null -eq $status.AntivirusSignatureLastUpdated) { $null } else { ([DateTimeOffset]$status.AntivirusSignatureLastUpdated).ToString('O') }
            QuickScanAge = [uint32]$status.QuickScanAge
            FullScanAge = [uint32]$status.FullScanAge
        } | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress
        """;

    private const string EventScript = """
        $ErrorActionPreference = 'Stop'
        $start = [DateTimeOffset]::Parse(
            $env:SOLTEX_EVENT_START_UTC,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime
        $maximum = [int]::Parse(
            $env:SOLTEX_EVENT_MAX,
            [System.Globalization.CultureInfo]::InvariantCulture)
        $ids = @(
            1000, 1001, 1002, 1006, 1007, 1008, 1015,
            1116, 1117, 1118, 1119,
            3002, 3007,
            5000, 5001, 5007, 5008, 5012, 5013
        )
        $events = @()
        try {
            $events = @(
            Microsoft.PowerShell.Diagnostics\Get-WinEvent -FilterHashtable @{
                LogName = 'Microsoft-Windows-Windows Defender/Operational'
                Id = $ids
                StartTime = $start
                } -MaxEvents $maximum -ErrorAction Stop
            )
        }
        catch {
            if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') {
                throw
            }
        }
        $items = @(
            $events |
                ForEach-Object {
                    [ordered]@{
                        EventId = [int]$_.Id
                        RecordId = [long]$_.RecordId
                        TimeCreatedUtc = $_.TimeCreated.ToUniversalTime().ToString('O')
                        Xml = $_.ToXml()
                    }
                }
        )
        [ordered]@{ Items = $items } | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress -Depth 4
        """;
}
