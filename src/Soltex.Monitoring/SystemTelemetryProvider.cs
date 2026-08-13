using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;

namespace Soltex.Monitoring;

public static class SystemTelemetryProvider
{
    public const int MaximumProcessCount = 32;
    public const int MaximumProcessNameLength = 80;
    public const int MaximumVolumeCount = 8;
    public const int MaximumNetworkInterfaceCount = 16;
    public const int MaximumNetworkNameLength = 96;
    private const int MaximumObservedProcessCount = 2_048;
    private const int MaximumObservedNetworkInterfaceCount = 256;
    private static readonly TimeSpan DefaultSampleWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MinimumSampleWindow = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumSampleWindow = TimeSpan.FromSeconds(2);

    public static async Task<SystemTelemetrySnapshot> CaptureAsync(
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
        Dictionary<int, ProcessSeed> firstProcesses = CaptureProcessSeeds(out int firstInaccessibleProcesses);
        Dictionary<string, NetworkSeed> firstNetworks = CaptureNetworkSeeds(out int firstInaccessibleNetworks);

        Stopwatch sample = Stopwatch.StartNew();
        await Task.Delay(window, cancellationToken).ConfigureAwait(false);
        sample.Stop();

        bool hasSecondSystemTimes = NativeTelemetry.TryReadSystemTimes(out SystemTimesSample secondSystemTimes);
        Dictionary<int, ProcessSeed> secondProcesses = CaptureProcessSeeds(out int secondInaccessibleProcesses);
        Dictionary<string, NetworkSeed> secondNetworks = CaptureNetworkSeeds(out int secondInaccessibleNetworks);
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
        List<StorageVolumeTelemetry> volumes = CaptureVolumes(out string? volumeLimitation);
        NetworkTelemetry? network = CalculateNetworkTelemetry(firstNetworks, secondNetworks, sample.Elapsed);
        ProcessTelemetry[] processes = CalculateProcessTelemetry(
            firstProcesses,
            secondProcesses,
            sample.Elapsed);

        List<string> limitations =
        [
            "GPU load, clocks, temperatures, and fan speed are unavailable until a separately supported provider is configured."
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

        int inaccessibleNetworks = firstInaccessibleNetworks + secondInaccessibleNetworks;
        if (network is null)
        {
            limitations.Add("Network throughput is unavailable because no stable active interface sample completed.");
        }
        else if (inaccessibleNetworks > 0)
        {
            limitations.Add("One or more network interfaces could not be sampled.");
        }

        bool hasAnyData =
            cpuPercent is not null || memory is not null || network is not null || processes.Length > 0;
        bool hasSupportedProviderFailure =
            cpuPercent is null ||
            memory is null ||
            volumeLimitation is not null ||
            network is null ||
            inaccessibleNetworks > 0;
        TelemetryObservationState state = DetermineObservationState(
            hasAnyData,
            hasSupportedProviderFailure);
        capture.Stop();
        return new SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            capture.Elapsed,
            state,
            cpuPercent,
            memory,
            volumes,
            network,
            processes,
            firstInaccessibleProcesses + secondInaccessibleProcesses,
            "GetSystemTimes · GlobalMemoryStatusEx · NetworkInterface statistics · System.Diagnostics.Process · DriveInfo",
            limitations);
    }

    internal static TelemetryObservationState DetermineObservationState(
        bool hasAnyData,
        bool hasSupportedProviderFailure) =>
        !hasAnyData
            ? TelemetryObservationState.Unavailable
            : hasSupportedProviderFailure
                ? TelemetryObservationState.Partial
                : TelemetryObservationState.Current;

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

    private static Dictionary<string, NetworkSeed> CaptureNetworkSeeds(out int inaccessibleCount)
    {
        inaccessibleCount = 0;
        Dictionary<string, NetworkSeed> snapshots = new(StringComparer.Ordinal);
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            inaccessibleCount++;
            return snapshots;
        }
        catch (PlatformNotSupportedException)
        {
            inaccessibleCount++;
            return snapshots;
        }

        foreach (NetworkInterface networkInterface in interfaces.Take(MaximumObservedNetworkInterfaceCount))
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                string.IsNullOrWhiteSpace(networkInterface.Id))
            {
                continue;
            }

            try
            {
                if (!HasUsableUnicastAddress(networkInterface))
                {
                    continue;
                }

                IPv4InterfaceStatistics statistics = networkInterface.GetIPv4Statistics();
                if (statistics.BytesReceived < 0 || statistics.BytesSent < 0)
                {
                    inaccessibleCount++;
                    continue;
                }

                snapshots[networkInterface.Id] = new NetworkSeed(
                    TelemetryMath.SanitizeNetworkName(networkInterface.Name),
                    networkInterface.NetworkInterfaceType.ToString(),
                    statistics.BytesReceived,
                    statistics.BytesSent);
            }
            catch (NetworkInformationException)
            {
                inaccessibleCount++;
            }
            catch (NotSupportedException)
            {
                inaccessibleCount++;
            }
        }

        if (interfaces.Length > MaximumObservedNetworkInterfaceCount)
        {
            inaccessibleCount += interfaces.Length - MaximumObservedNetworkInterfaceCount;
        }

        return snapshots;
    }

    private static bool HasUsableUnicastAddress(NetworkInterface networkInterface)
    {
        IPInterfaceProperties properties = networkInterface.GetIPProperties();
        return properties.UnicastAddresses.Any(addressInformation =>
            addressInformation.Address.AddressFamily is
                AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
            !IPAddress.IsLoopback(addressInformation.Address));
    }

    private static NetworkTelemetry? CalculateNetworkTelemetry(
        IReadOnlyDictionary<string, NetworkSeed> first,
        IReadOnlyDictionary<string, NetworkSeed> second,
        TimeSpan elapsed)
    {
        List<NetworkInterfaceTelemetry> interfaces = [];
        long receiveTotal = 0;
        long sendTotal = 0;
        foreach ((string interfaceId, NetworkSeed current) in second)
        {
            if (!first.TryGetValue(interfaceId, out NetworkSeed prior))
            {
                continue;
            }

            long? receiveRate = TelemetryMath.CalculateByteRate(
                prior.BytesReceived,
                current.BytesReceived,
                elapsed);
            long? sendRate = TelemetryMath.CalculateByteRate(
                prior.BytesSent,
                current.BytesSent,
                elapsed);
            if (receiveRate is null || sendRate is null)
            {
                continue;
            }

            receiveTotal = SaturatingAdd(receiveTotal, receiveRate.Value);
            sendTotal = SaturatingAdd(sendTotal, sendRate.Value);
            interfaces.Add(new NetworkInterfaceTelemetry(
                current.Name,
                current.InterfaceType,
                receiveRate.Value,
                sendRate.Value));
        }

        if (interfaces.Count == 0)
        {
            return null;
        }

        NetworkInterfaceTelemetry[] visibleInterfaces = interfaces
            .OrderByDescending(item => item.TotalBytesPerSecond)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumNetworkInterfaceCount)
            .ToArray();
        return new NetworkTelemetry(receiveTotal, sendTotal, visibleInterfaces);
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static ProcessTelemetry[] CalculateProcessTelemetry(
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

    private static List<StorageVolumeTelemetry> CaptureVolumes(out string? limitation)
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

    private readonly record struct ProcessSeed(
        string Name,
        TimeSpan ProcessorTime,
        long WorkingSetBytes,
        int ThreadCount);

    private readonly record struct NetworkSeed(
        string Name,
        string InterfaceType,
        long BytesReceived,
        long BytesSent);
}
