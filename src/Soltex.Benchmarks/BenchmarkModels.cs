namespace Soltex.Benchmarks;

public sealed record BenchmarkProfile(
    string Id,
    int Version,
    TimeSpan CpuDuration,
    TimeSpan MemoryDuration,
    int StorageBytes)
{
    public static BenchmarkProfile Quick { get; } = new(
        "quick-local",
        Version: 1,
        CpuDuration: TimeSpan.FromMilliseconds(1500),
        MemoryDuration: TimeSpan.FromMilliseconds(1200),
        StorageBytes: 32 * 1024 * 1024);
}

public sealed record BenchmarkMetric(
    string Id,
    string Label,
    double Value,
    string Unit,
    TimeSpan Duration,
    string Detail);

public sealed record BenchmarkResult(
    string ProfileId,
    int ProfileVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int ProcessorCount,
    double? BaselineCpuPercent,
    BenchmarkMetric Cpu,
    BenchmarkMetric Memory,
    BenchmarkMetric StorageWrite,
    BenchmarkMetric StorageRead,
    IReadOnlyList<string> Limitations);

public static class BenchmarkResultContract
{
    public static IReadOnlyList<string> Limitations { get; } = Array.AsReadOnly(
        new[]
        {
            "Local diagnostic only; results are not a cross-machine score.",
            "Temperature, power, fan, and GPU guardrails are unavailable.",
            "Storage results use a temporary file and may be influenced by the Windows cache."
        });
}
