using System.Collections.ObjectModel;

namespace Soltex.Monitoring;

public enum TelemetryObservationState
{
    Current,
    Partial,
    Stale,
    Unavailable
}

public sealed record MemoryTelemetry(
    ulong TotalBytes,
    ulong AvailableBytes,
    double UsedPercent)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;
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

    public IReadOnlyList<ProcessTelemetry> Processes { get; }

    public int InaccessibleProcessCount { get; }

    public string Provenance { get; }

    public IReadOnlyList<string> Limitations { get; }
}
