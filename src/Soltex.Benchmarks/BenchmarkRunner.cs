using System.Diagnostics;
using System.Security.Cryptography;

namespace Soltex.Benchmarks;

public static class BenchmarkRunner
{
    internal const int CpuBufferBytes = 1024 * 1024;
    internal const int MemoryBufferBytes = 8 * 1024 * 1024;
    internal const int StorageBlockBytes = 1024 * 1024;
    internal const int MaximumStorageBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan MinimumStageDuration = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaximumStageDuration = TimeSpan.FromSeconds(5);

    public static Task<BenchmarkResult> RunAsync(
        BenchmarkProfile profile,
        string scratchDirectory,
        double? baselineCpuPercent,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        string safeScratch = PrepareScratchDirectory(scratchDirectory);
        return Task.Run(
            () => Run(profile, safeScratch, NormalizeBaseline(baselineCpuPercent), cancellationToken),
            cancellationToken);
    }

    internal static void ValidateProfile(BenchmarkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id) ||
            profile.Id.Length > 40 ||
            profile.Id.Any(char.IsControl) ||
            profile.Version is < 1 or > 100 ||
            profile.CpuDuration < MinimumStageDuration ||
            profile.CpuDuration > MaximumStageDuration ||
            profile.MemoryDuration < MinimumStageDuration ||
            profile.MemoryDuration > MaximumStageDuration ||
            profile.StorageBytes is < StorageBlockBytes or > MaximumStorageBytes ||
            profile.StorageBytes % StorageBlockBytes != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "The benchmark profile is outside its safety bounds.");
        }
    }

    private static BenchmarkResult Run(
        BenchmarkProfile profile,
        string scratchDirectory,
        double? baselineCpuPercent,
        CancellationToken cancellationToken)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        BenchmarkMetric cpu = MeasureCpu(profile.CpuDuration, cancellationToken);
        BenchmarkMetric memory = MeasureMemory(profile.MemoryDuration, cancellationToken);
        (BenchmarkMetric write, BenchmarkMetric read) = MeasureStorage(
            scratchDirectory,
            profile.StorageBytes,
            cancellationToken);
        return new BenchmarkResult(
            profile.Id,
            profile.Version,
            started,
            DateTimeOffset.UtcNow,
            Environment.ProcessorCount,
            baselineCpuPercent,
            cpu,
            memory,
            write,
            read,
            BenchmarkResultContract.Limitations);
    }

    private static BenchmarkMetric MeasureCpu(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        byte[] input = new byte[CpuBufferBytes];
        RandomNumberGenerator.Fill(input);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        long bytes = 0;
        byte checksum = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SHA256.HashData(input, digest);
            checksum ^= digest[0];
            bytes += input.Length;
        }

        stopwatch.Stop();
        double throughput = MegabytesPerSecond(bytes, stopwatch.Elapsed);
        return new BenchmarkMetric(
            "cpu-sha256",
            "SHA-256 throughput",
            throughput,
            "MiB/s",
            stopwatch.Elapsed,
            $"1 MiB blocks · checksum {checksum:X2}");
    }

    private static BenchmarkMetric MeasureMemory(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        byte[] source = new byte[MemoryBufferBytes];
        byte[] destination = new byte[MemoryBufferBytes];
        RandomNumberGenerator.Fill(source.AsSpan(0, 4096));
        long bytes = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(source, 0, destination, 0, source.Length);
            Buffer.BlockCopy(destination, 0, source, 0, source.Length);
            bytes += source.Length * 2L;
        }

        stopwatch.Stop();
        double throughput = MegabytesPerSecond(bytes, stopwatch.Elapsed);
        return new BenchmarkMetric(
            "memory-copy",
            "Buffer copy throughput",
            throughput,
            "MiB/s",
            stopwatch.Elapsed,
            "8 MiB managed buffers · two-way copy");
    }

    private static (BenchmarkMetric Write, BenchmarkMetric Read) MeasureStorage(
        string scratchDirectory,
        int storageBytes,
        CancellationToken cancellationToken)
    {
        using OwnedScratchRun scratch = OwnedScratchRun.Create(scratchDirectory);
        byte[] block = new byte[StorageBlockBytes];
        RandomNumberGenerator.Fill(block.AsSpan(0, 4096));
        using FileStream stream = scratch.CreatePayloadStream();

        Stopwatch writeWatch = Stopwatch.StartNew();
        int remaining = storageBytes;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(block.Length, remaining);
            stream.Write(block, 0, count);
            remaining -= count;
        }

        stream.Flush(flushToDisk: true);
        writeWatch.Stop();
        ValidateExpectedLength(stream.Length, storageBytes);

        stream.Position = 0;
        Stopwatch readWatch = Stopwatch.StartNew();
        long readBytes = 0;
        while (readBytes < storageBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = Math.Min(block.Length, storageBytes - (int)readBytes);
            int read = stream.Read(block, 0, requested);
            if (read == 0)
            {
                break;
            }

            readBytes += read;
        }

        readWatch.Stop();
        ValidateExpectedLength(readBytes, storageBytes);
        ValidateExpectedLength(stream.Length, storageBytes);

        scratch.VerifyIdentity();
        return (
            new BenchmarkMetric(
                "storage-write",
                "Temporary write",
                MegabytesPerSecond(storageBytes, writeWatch.Elapsed),
                "MiB/s",
                writeWatch.Elapsed,
                $"{storageBytes / (1024 * 1024)} MiB · flush-to-disk requested"),
            new BenchmarkMetric(
                "storage-read",
                "Temporary read",
                MegabytesPerSecond(readBytes, readWatch.Elapsed),
                "MiB/s",
                readWatch.Elapsed,
                $"{readBytes / (1024 * 1024)} MiB · Windows cache may contribute"));
    }

    private static string PrepareScratchDirectory(string scratchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        string fullPath = Path.GetFullPath(scratchDirectory);
        Directory.CreateDirectory(fullPath);
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The benchmark scratch root must be a normal local directory.");
        }

        return fullPath;
    }

    internal static void ValidateExpectedLength(long actualLength, int expectedLength)
    {
        if (actualLength != expectedLength)
        {
            throw new InvalidDataException(
                $"The benchmark scratch artifact length was {actualLength}; expected {expectedLength} bytes.");
        }
    }

    private static double? NormalizeBaseline(double? value) =>
        value is double cpu && double.IsFinite(cpu)
            ? Math.Clamp(cpu, 0, 100)
            : null;

    private static double MegabytesPerSecond(long bytes, TimeSpan elapsed)
    {
        if (bytes <= 0 || elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        return bytes / 1024d / 1024d / elapsed.TotalSeconds;
    }

    private sealed class OwnedScratchRun : IDisposable
    {
        private const string OwnerFileName = ".soltex-owner";
        private const string PayloadFileName = "payload.tmp";
        private readonly string _directoryPath;
        private readonly string _ownerPath;
        private readonly DateTime _creationTimeUtc;
        private FileStream? _ownerStream;
        private bool _payloadCreated;
        private bool _disposed;

        private OwnedScratchRun(
            string directoryPath,
            string ownerPath,
            DateTime creationTimeUtc,
            FileStream ownerStream)
        {
            _directoryPath = directoryPath;
            _ownerPath = ownerPath;
            _creationTimeUtc = creationTimeUtc;
            _ownerStream = ownerStream;
        }

        internal static OwnedScratchRun Create(string scratchDirectory)
        {
            string root = PrepareScratchDirectory(scratchDirectory);
            string directoryPath = Path.Combine(
                root,
                "run-" + Guid.NewGuid().ToString("N"));
            DirectoryInfo directory = Directory.CreateDirectory(directoryPath);
            EnsureNormalDirectory(directoryPath, directory.CreationTimeUtc);
            string ownerPath = Path.Combine(directoryPath, OwnerFileName);
            FileStream? ownerStream = null;
            try
            {
                ownerStream = new FileStream(
                    ownerPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                byte[] ownerToken = RandomNumberGenerator.GetBytes(32);
                ownerStream.Write(ownerToken);
                ownerStream.Flush(flushToDisk: true);
                return new OwnedScratchRun(
                    directoryPath,
                    ownerPath,
                    directory.CreationTimeUtc,
                    ownerStream);
            }
            catch
            {
                ownerStream?.Dispose();
                DeleteEmptyNormalDirectory(directoryPath);
                throw;
            }
        }

        internal FileStream CreatePayloadStream()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_payloadCreated)
            {
                throw new InvalidOperationException("The benchmark run already owns a scratch payload.");
            }

            VerifyIdentity();
            _payloadCreated = true;
            return new FileStream(
                Path.Combine(_directoryPath, PayloadFileName),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                StorageBlockBytes,
                FileOptions.SequentialScan | FileOptions.WriteThrough | FileOptions.DeleteOnClose);
        }

        internal void VerifyIdentity()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureNormalDirectory(_directoryPath, _creationTimeUtc);
            if (_ownerStream is not null &&
                !_ownerStream.SafeFileHandle.IsInvalid &&
                !_ownerStream.SafeFileHandle.IsClosed)
            {
                return;
            }

            throw new InvalidDataException("The benchmark scratch owner handle became invalid.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            bool ownsOnlyExpectedEntries = false;
            try
            {
                EnsureNormalDirectory(_directoryPath, _creationTimeUtc);
                string[] entries = Directory.GetFileSystemEntries(_directoryPath);
                ownsOnlyExpectedEntries = entries.All(entry =>
                    string.Equals(entry, _ownerPath, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _ownerStream?.Dispose();
                _ownerStream = null;
            }

            if (!ownsOnlyExpectedEntries)
            {
                throw new InvalidDataException(
                    "The benchmark scratch directory changed; Soltex left it in place for review.");
            }

            EnsureNormalDirectory(_directoryPath, _creationTimeUtc);
            if (Directory.EnumerateFileSystemEntries(_directoryPath).Any())
            {
                throw new InvalidDataException(
                    "The benchmark scratch directory was not empty after owned cleanup.");
            }

            Directory.Delete(_directoryPath, recursive: false);
        }

        private static void EnsureNormalDirectory(string path, DateTime expectedCreationTimeUtc)
        {
            DirectoryInfo directory = new(path);
            directory.Refresh();
            if (!directory.Exists ||
                (directory.Attributes & FileAttributes.Directory) == 0 ||
                (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                directory.CreationTimeUtc != expectedCreationTimeUtc)
            {
                throw new InvalidDataException("The benchmark scratch directory identity changed.");
            }
        }

        private static void DeleteEmptyNormalDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0 &&
                (attributes & FileAttributes.ReparsePoint) == 0 &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
    }
}
