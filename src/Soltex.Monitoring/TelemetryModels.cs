using System.Collections.ObjectModel;

namespace Soltex.Monitoring;

public enum TelemetryObservationState
{
    Current,
    Partial,
    Stale,
    Unavailable
}

/// <summary>
/// Physical memory plus the system commit charge. Commit is reported separately
/// because it counts reserved backing store, not resident pages, and can exceed
/// installed RAM.
/// </summary>
public sealed record MemoryTelemetry(
    ulong TotalBytes,
    ulong AvailableBytes,
    double UsedPercent,
    ulong CommitLimitBytes = 0,
    ulong CommitAvailableBytes = 0)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public bool HasCommitCharge => CommitLimitBytes > 0;

    public ulong CommitUsedBytes => CommitLimitBytes >= CommitAvailableBytes
        ? CommitLimitBytes - CommitAvailableBytes
        : 0;

    public double CommitUsedPercent => CommitLimitBytes == 0
        ? 0
        : Math.Clamp((double)CommitUsedBytes / CommitLimitBytes * 100, 0, 100);
}

public sealed record StorageVolumeTelemetry(
    string Name,
    long TotalBytes,
    long AvailableBytes)
{
    public long UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)UsedBytes / TotalBytes * 100, 0, 100);
}

public sealed record NetworkInterfaceTelemetry(
    string Name,
    string InterfaceType,
    long ReceiveBytesPerSecond,
    long SendBytesPerSecond)
{
    public long TotalBytesPerSecond => SaturatingAdd(ReceiveBytesPerSecond, SendBytesPerSecond);

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}

public sealed class NetworkTelemetry
{
    internal NetworkTelemetry(
        long receiveBytesPerSecond,
        long sendBytesPerSecond,
        IReadOnlyList<NetworkInterfaceTelemetry> interfaces)
    {
        ReceiveBytesPerSecond = Math.Max(0, receiveBytesPerSecond);
        SendBytesPerSecond = Math.Max(0, sendBytesPerSecond);
        Interfaces = new ReadOnlyCollection<NetworkInterfaceTelemetry>(interfaces.ToArray());
    }

    public long ReceiveBytesPerSecond { get; }

    public long SendBytesPerSecond { get; }

    public long TotalBytesPerSecond =>
        ReceiveBytesPerSecond > long.MaxValue - SendBytesPerSecond
            ? long.MaxValue
            : ReceiveBytesPerSecond + SendBytesPerSecond;

    public IReadOnlyList<NetworkInterfaceTelemetry> Interfaces { get; }
}

public sealed record ProcessTelemetry(
    int ProcessId,
    string Name,
    double CpuPercent,
    long WorkingSetBytes,
    int ThreadCount);

public sealed class SystemTelemetrySnapshot
{
    internal SystemTelemetrySnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan captureDuration,
        TelemetryObservationState state,
        double? cpuPercent,
        MemoryTelemetry? memory,
        IReadOnlyList<StorageVolumeTelemetry> volumes,
        NetworkTelemetry? network,
        IReadOnlyList<ProcessTelemetry> processes,
        int inaccessibleProcessCount,
        string provenance,
        IReadOnlyList<string> limitations)
    {
        CapturedAtUtc = capturedAtUtc;
        CaptureDuration = captureDuration;
        State = state;
        CpuPercent = cpuPercent;
        Memory = memory;
        Volumes = new ReadOnlyCollection<StorageVolumeTelemetry>(volumes.ToArray());
        Network = network;
        Processes = new ReadOnlyCollection<ProcessTelemetry>(processes.ToArray());
        InaccessibleProcessCount = inaccessibleProcessCount;
        Provenance = provenance;
        Limitations = new ReadOnlyCollection<string>(limitations.ToArray());
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public TimeSpan CaptureDuration { get; }

    public TelemetryObservationState State { get; }

    public double? CpuPercent { get; }

    public MemoryTelemetry? Memory { get; }

    public IReadOnlyList<StorageVolumeTelemetry> Volumes { get; }

    public NetworkTelemetry? Network { get; }

    public IReadOnlyList<ProcessTelemetry> Processes { get; }

    public int InaccessibleProcessCount { get; }

    public string Provenance { get; }

    public IReadOnlyList<string> Limitations { get; }
}
