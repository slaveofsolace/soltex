using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using Soltex.Benchmarks;

namespace Soltex.App;

internal sealed record BenchmarkResultLoad(
    BenchmarkResult? Result,
    bool RecoveredFromInvalid,
    string Detail);

internal sealed class BenchmarkResultStore
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumDocumentBytes = 32 * 1024;
    private const double MaximumMetricValue = 1_000_000_000;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    internal BenchmarkResultStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal BenchmarkResultLoad Load()
    {
        try
        {
            using FileStream stream = new(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            byte[] json = new byte[MaximumDocumentBytes + 1];
            int length = ReadBounded(stream, json);
            if (length > MaximumDocumentBytes)
            {
                return Recovered("The saved benchmark result exceeded its size bound and was not loaded.");
            }

            BenchmarkResultDocument? document =
                JsonSerializer.Deserialize<BenchmarkResultDocument>(
                    json.AsSpan(0, length),
                    SerializerOptions);
            if (!TryCreateResult(document, out BenchmarkResult? result) || result is null)
            {
                return Recovered("The saved benchmark result was invalid and was not loaded.");
            }

            return new BenchmarkResultLoad(result, false, Describe(result));
        }
        catch (FileNotFoundException)
        {
            return new BenchmarkResultLoad(null, false, "No saved benchmark result.");
        }
        catch (DirectoryNotFoundException)
        {
            return new BenchmarkResultLoad(null, false, "No saved benchmark result.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return Recovered("The saved benchmark result could not be read and was not loaded.");
        }
    }

    internal void Save(BenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        BenchmarkResultDocument document = CreateDocument(result);
        if (!TryCreateResult(document, out _))
        {
            throw new InvalidOperationException("The benchmark result is outside its persistence bounds.");
        }

        string json = JsonSerializer.Serialize(document, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The benchmark result exceeded its document-size bound.");
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The benchmark result requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    internal static string Describe(BenchmarkResult result) =>
        $"Saved local result from {result.CompletedAtUtc.ToLocalTime():g}.";

    private static int ReadBounded(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static BenchmarkResultLoad Recovered(string detail) =>
        new(null, true, detail);

    private static BenchmarkResultDocument CreateDocument(BenchmarkResult result) => new(
        CurrentSchemaVersion,
        result.ProfileId,
        result.ProfileVersion,
        result.StartedAtUtc,
        result.CompletedAtUtc,
        result.ProcessorCount,
        result.BaselineCpuPercent,
        result.Cpu.Value,
        result.Cpu.Duration.TotalMilliseconds,
        result.Memory.Value,
        result.Memory.Duration.TotalMilliseconds,
        result.StorageWrite.Value,
        result.StorageWrite.Duration.TotalMilliseconds,
        result.StorageRead.Value,
        result.StorageRead.Duration.TotalMilliseconds);

    private static bool TryCreateResult(
        BenchmarkResultDocument? document,
        out BenchmarkResult? result)
    {
        result = null;
        if (document is null ||
            document.SchemaVersion != CurrentSchemaVersion ||
            !string.Equals(document.ProfileId, BenchmarkProfile.Quick.Id, StringComparison.Ordinal) ||
            document.ProfileVersion != BenchmarkProfile.Quick.Version ||
            document.StartedAtUtc == default ||
            document.CompletedAtUtc < document.StartedAtUtc ||
            document.CompletedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
            document.ProcessorCount is < 1 or > 4096 ||
            document.BaselineCpuPercent is double baseline &&
                (!double.IsFinite(baseline) || baseline is < 0 or > 100) ||
            !MetricValid(document.CpuValue, document.CpuDurationMilliseconds) ||
            !MetricValid(document.MemoryValue, document.MemoryDurationMilliseconds) ||
            !MetricValid(document.StorageWriteValue, document.StorageWriteDurationMilliseconds) ||
            !MetricValid(document.StorageReadValue, document.StorageReadDurationMilliseconds))
        {
            return false;
        }

        result = new BenchmarkResult(
            BenchmarkProfile.Quick.Id,
            document.ProfileVersion,
            document.StartedAtUtc,
            document.CompletedAtUtc,
            document.ProcessorCount,
            document.BaselineCpuPercent,
            Metric("cpu-sha256", "SHA-256 throughput", document.CpuValue, document.CpuDurationMilliseconds, "1 MiB blocks · saved local run"),
            Metric("memory-copy", "Buffer copy throughput", document.MemoryValue, document.MemoryDurationMilliseconds, "8 MiB buffers · saved local run"),
            Metric("storage-write", "Temporary write", document.StorageWriteValue, document.StorageWriteDurationMilliseconds, "32 MiB · saved local run"),
            Metric("storage-read", "Temporary read", document.StorageReadValue, document.StorageReadDurationMilliseconds, "32 MiB · saved local run"),
            BenchmarkResultContract.Limitations);
        return true;
    }

    private static BenchmarkMetric Metric(
        string id,
        string label,
        double value,
        double durationMilliseconds,
        string detail) =>
        new(id, label, value, "MiB/s", TimeSpan.FromMilliseconds(durationMilliseconds), detail);

    private static bool MetricValid(double value, double durationMilliseconds) =>
        double.IsFinite(value) && value is > 0 and <= MaximumMetricValue &&
        double.IsFinite(durationMilliseconds) && durationMilliseconds is > 0 and <= 30_000;

    private sealed record BenchmarkResultDocument(
        int SchemaVersion,
        string? ProfileId,
        int ProfileVersion,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        int ProcessorCount,
        double? BaselineCpuPercent,
        double CpuValue,
        double CpuDurationMilliseconds,
        double MemoryValue,
        double MemoryDurationMilliseconds,
        double StorageWriteValue,
        double StorageWriteDurationMilliseconds,
        double StorageReadValue,
        double StorageReadDurationMilliseconds);
}
