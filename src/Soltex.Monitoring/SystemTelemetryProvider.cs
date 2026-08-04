using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace Soltex.Monitoring;

public sealed class SystemTelemetryProvider
{
    public const int MaximumProcessCount = 32;
    public const int MaximumProcessNameLength = 80;
    public const int MaximumVolumeCount = 8;
    private const int MaximumObservedProcessCount = 2_048;
    private static readonly TimeSpan DefaultSampleWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MinimumSampleWindow = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumSampleWindow = TimeSpan.FromSeconds(2);

    public async Task<SystemTelemetrySnapshot> CaptureAsync(
        TimeSpan? sampleWindow = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan window = sampleWindow ?? DefaultSampleWindow;
        if (window < MinimumSampleWindow || window > MaximumSampleWindow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleWindow),
                $"The sample window must be between {MinimumSampleWindow.TotalMilliseconds:F0} ms and {MaximumSampleWindow.TotalSeconds:F0} seconds.");
        }

        Stopwatch capture = Stopwatch.StartNew();
        bool hasFirstSystemTimes = NativeTelemetry.TryReadSystemTimes(out SystemTimesSample firstSystemTimes);
        Dictionary<int, ProcessSeed> firstProcesses = CaptureProcessSeeds(out int firstInaccessible);

        Stopwatch sample = Stopwatch.StartNew();
        await Task.Delay(window, cancellationToken).ConfigureAwait(false);
        sample.Stop();

        bool hasSecondSystemTimes = NativeTelemetry.TryReadSystemTimes(out SystemTimesSample secondSystemTimes);
        Dictionary<int, ProcessSeed> secondProcesses = CaptureProcessSeeds(out int secondInaccessible);
        double? cpuPercent = hasFirstSystemTimes && hasSecondSystemTimes
            ? TelemetryMath.CalculateSystemUsage(
                firstSystemTimes.Idle,
                firstSystemTimes.Kernel,
                firstSystemTimes.User,
                secondSystemTimes.Idle,
                secondSystemTimes.Kernel,
                secondSystemTimes.User)
            : null;

        _ = NativeTelemetry.TryReadMemory(out MemoryTelemetry? memory);
        IReadOnlyList<StorageVolumeTelemetry> volumes = CaptureVolumes(out string? volumeLimitation);
        IReadOnlyList<ProcessTelemetry> processes = CalculateProcessTelemetry(
            firstProcesses,
            secondProcesses,
            sample.Elapsed);

        List<string> limitations =
        [
            "GPU load, clocks, temperatures, and fan speed are unavailable until a separately supported provider is configured.",
            "Network throughput is not sampled in this first bounded provider."
        ];
        if (cpuPercent is null)
        {
            limitations.Add("CPU timing is unavailable from GetSystemTimes.");
        }

        if (memory is null)
        {
            limitations.Add("Physical-memory status is unavailable from GlobalMemoryStatusEx.");
        }

        if (volumeLimitation is not null)
        {
            limitations.Add(volumeLimitation);
        }

        TelemetryObservationState state = cpuPercent is not null || memory is not null || processes.Count > 0
            ? limitations.Count == 0 ? TelemetryObservationState.Current : TelemetryObservationState.Partial
            : TelemetryObservationState.Unavailable;
        capture.Stop();
        return new SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            capture.Elapsed,
            state,
            cpuPercent,
            memory,
            volumes,
            processes,
            firstInaccessible + secondInaccessible,
            "GetSystemTimes · GlobalMemoryStatusEx · System.Diagnostics.Process · DriveInfo",
            limitations);
    }

    private static Dictionary<int, ProcessSeed> CaptureProcessSeeds(out int inaccessibleCount)
    {
        inaccessibleCount = 0;
        Dictionary<int, ProcessSeed> snapshots = [];
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (InvalidOperationException)
        {
            return snapshots;
        }
        catch (Win32Exception)
        {
            return snapshots;
        }

        for (int index = 0; index < processes.Length; index++)
        {
            using Process process = processes[index];
            if (index >= MaximumObservedProcessCount)
            {
                inaccessibleCount++;
                continue;
            }

            try
            {
                snapshots[process.Id] = new ProcessSeed(
                    TelemetryMath.SanitizeProcessName(process.ProcessName),
                    process.TotalProcessorTime,
                    Math.Max(0, process.WorkingSet64),
                    Math.Max(0, process.Threads.Count));
            }
            catch (InvalidOperationException)
            {
                inaccessibleCount++;
            }
            catch (Win32Exception)
            {
                inaccessibleCount++;
            }
            catch (NotSupportedException)
            {
                inaccessibleCount++;
            }
        }

        return snapshots;
    }

    private static IReadOnlyList<ProcessTelemetry> CalculateProcessTelemetry(
        IReadOnlyDictionary<int, ProcessSeed> first,
        IReadOnlyDictionary<int, ProcessSeed> second,
        TimeSpan elapsed)
    {
        List<ProcessTelemetry> telemetry = [];
        foreach ((int processId, ProcessSeed current) in second)
        {
            double cpuPercent = first.TryGetValue(processId, out ProcessSeed prior)
                ? TelemetryMath.CalculateProcessUsage(
                    prior.ProcessorTime,
                    current.ProcessorTime,
                    elapsed,
                    Environment.ProcessorCount)
                : 0;
            telemetry.Add(new ProcessTelemetry(
                processId,
                current.Name,
                cpuPercent,
                current.WorkingSetBytes,
                current.ThreadCount));
        }

        return telemetry
            .OrderByDescending(process => process.CpuPercent)
            .ThenByDescending(process => process.WorkingSetBytes)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumProcessCount)
            .ToArray();
    }

    private static IReadOnlyList<StorageVolumeTelemetry> CaptureVolumes(out string? limitation)
    {
        limitation = null;
        List<StorageVolumeTelemetry> volumes = [];
        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives()
                         .Where(candidate => candidate.DriveType == DriveType.Fixed)
                         .Take(MaximumVolumeCount))
            {
                try
                {
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        volumes.Add(new StorageVolumeTelemetry(
                            drive.Name,
                            drive.TotalSize,
                            Math.Min(drive.AvailableFreeSpace, drive.TotalSize)));
                    }
                }
                catch (IOException)
                {
                    limitation = "One or more fixed volumes could not be observed.";
                }
                catch (UnauthorizedAccessException)
                {
                    limitation = "One or more fixed volumes denied observation.";
                }
            }
        }
        catch (IOException)
        {
            limitation = "Fixed-volume inventory is unavailable.";
        }
        catch (UnauthorizedAccessException)
        {
            limitation = "Fixed-volume inventory denied access.";
        }
        catch (SecurityException)
        {
            limitation = "Fixed-volume inventory is blocked by policy.";
        }

        return volumes;
    }

    private sealed record ProcessSeed(
        string Name,
        TimeSpan ProcessorTime,
        long WorkingSetBytes,
        int ThreadCount);
}
