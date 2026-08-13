using System.Diagnostics;
using Soltex.Benchmarks;

namespace Soltex.Benchmarks.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        List<(string Name, Func<Task> Test)> tests =
        [
            ("Quick profile is short and storage-bounded", QuickProfileIsBoundedAsync),
            ("Production quick profile completes and cleans scratch", ProductionQuickProfileCompletesAsync),
            ("Live benchmark reports measured stages and cleans scratch", LiveRunIsMeasuredAsync),
            ("Cancellation stops the benchmark and cleans scratch", CancellationCleansScratchAsync),
            ("Unexpected scratch lengths fail closed", UnexpectedScratchLengthFailsClosedAsync),
            ("Unsafe profiles and scratch roots fail closed", UnsafeInputsFailClosedAsync)
        ];
        int failures = 0;
        Stopwatch suite = Stopwatch.StartNew();
        foreach ((string name, Func<Task> test) in tests)
        {
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                await test();
                Console.WriteLine($"PASS  {name} ({timer.Elapsed.TotalMilliseconds:F1} ms)");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
        Console.WriteLine($"MEASURE benchmark_suite tests={tests.Count} failed={failures} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
        return failures == 0 ? 0 : 1;
    }

    private static Task QuickProfileIsBoundedAsync()
    {
        BenchmarkProfile profile = BenchmarkProfile.Quick;
        True(profile.CpuDuration <= TimeSpan.FromSeconds(2), "CPU stage exceeded the quick bound.");
        True(profile.MemoryDuration <= TimeSpan.FromSeconds(2), "Memory stage exceeded the quick bound.");
        True(profile.StorageBytes == 32 * 1024 * 1024, "Storage stage drifted from 32 MiB.");
        BenchmarkRunner.ValidateProfile(profile);
        return Task.CompletedTask;
    }

    private static async Task ProductionQuickProfileCompletesAsync()
    {
        string root = CreateScratch();
        try
        {
            Stopwatch timer = Stopwatch.StartNew();
            BenchmarkResult result = await BenchmarkRunner.RunAsync(
                BenchmarkProfile.Quick,
                root,
                0);

            True(result.ProfileId == BenchmarkProfile.Quick.Id,
                "Production run did not retain the quick-profile identity.");
            True(result.CompletedAtUtc >= result.StartedAtUtc,
                "Production run reported an invalid time range.");
            True(result.Cpu.Value > 0 && result.Memory.Value > 0 &&
                 result.StorageWrite.Value > 0 && result.StorageRead.Value > 0,
                "Production run did not measure every named stage.");
            True(timer.Elapsed < TimeSpan.FromSeconds(15),
                "Production quick profile exceeded its end-to-end time bound.");
            True(!Directory.EnumerateFileSystemEntries(root).Any(),
                "Production quick profile left owned scratch state behind.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task LiveRunIsMeasuredAsync()
    {
        string root = CreateScratch();
        try
        {
            BenchmarkProfile profile = TestProfile();
            BenchmarkResult result = await BenchmarkRunner.RunAsync(profile, root, 12.5);
            True(result.ProfileId == profile.Id && result.ProfileVersion == profile.Version,
                "Result did not retain profile identity.");
            True(result.BaselineCpuPercent == 12.5 && result.ProcessorCount > 0,
                "Result did not retain bounded environment context.");
            foreach (BenchmarkMetric metric in new[]
                     {
                         result.Cpu,
                         result.Memory,
                         result.StorageWrite,
                         result.StorageRead
                     })
            {
                True(double.IsFinite(metric.Value) && metric.Value > 0,
                    $"{metric.Id} did not report positive finite throughput.");
                True(metric.Duration > TimeSpan.Zero, $"{metric.Id} did not report a duration.");
            }

            True(!Directory.EnumerateFileSystemEntries(root).Any(),
                "Benchmark left owned scratch state behind.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CancellationCleansScratchAsync()
    {
        string root = CreateScratch();
        try
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(35));
            await ThrowsAsync<OperationCanceledException>(() =>
                BenchmarkRunner.RunAsync(
                    new BenchmarkProfile(
                        "cancel-test",
                        1,
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        4 * 1024 * 1024),
                    root,
                    null,
                    cancellation.Token));
            True(!Directory.EnumerateFileSystemEntries(root).Any(),
                "Cancelled benchmark left owned scratch state behind.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task UnexpectedScratchLengthFailsClosedAsync()
    {
        await ThrowsAsync<InvalidDataException>(() => Task.Run(() =>
            BenchmarkRunner.ValidateExpectedLength(1023, 1024)));
        await ThrowsAsync<InvalidDataException>(() => Task.Run(() =>
            BenchmarkRunner.ValidateExpectedLength(1025, 1024)));
        BenchmarkRunner.ValidateExpectedLength(1024, 1024);
    }

    private static async Task UnsafeInputsFailClosedAsync()
    {
        string root = CreateScratch();
        try
        {
            await ThrowsAsync<ArgumentOutOfRangeException>(() =>
                BenchmarkRunner.RunAsync(
                    new BenchmarkProfile("oversized", 1, TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(30), 1024 * 1024),
                    root,
                    null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        await ThrowsAsync<ArgumentException>(() =>
            BenchmarkRunner.RunAsync(TestProfile(), " ", null));
    }

    private static BenchmarkProfile TestProfile() => new(
        "test-local",
        1,
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(40),
        1024 * 1024);

    private static string CreateScratch()
    {
        string root = Path.Combine(Path.GetTempPath(), "soltex-benchmark-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
