using System.Diagnostics;
using Soltex.Monitoring;

List<(string Name, Func<Task> Test)> tests =
[
    ("System CPU math reports bounded busy time", SystemCpuMathIsBoundedAsync),
    ("System CPU math rejects regressing counters", SystemCpuMathRejectsRegressionAsync),
    ("Process CPU math respects machine capacity", ProcessCpuMathRespectsCapacityAsync),
    ("Process names remove control characters and enforce bounds", ProcessNamesAreSanitizedAsync),
    ("Telemetry history validates capacity and samples", TelemetryHistoryValidatesInputAsync),
    ("Telemetry history is bounded and snapshots are immutable", TelemetryHistoryIsBoundedAsync),
    ("Telemetry history clamps percentages", TelemetryHistoryClampsAsync),
    ("Sample windows reject unsafe bounds", SampleWindowsAreBoundedAsync),
    ("Cancellation stops a pending sample", PendingSampleCanBeCancelledAsync),
    ("Live Windows capture is bounded and provenance-labeled", LiveCaptureIsBoundedAsync),
    ("Live process rows expose no paths", LiveProcessRowsExposeNoPathsAsync),
    ("Live memory and volume percentages are bounded", LivePercentagesAreBoundedAsync),
    ("Monitoring capture overhead is measured", CapturePerformanceIsMeasuredAsync)
];

int failed = 0;
Stopwatch suite = Stopwatch.StartNew();
foreach ((string name, Func<Task> test) in tests)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        await test();
        stopwatch.Stop();
        Console.WriteLine($"PASS  {name} ({stopwatch.Elapsed.TotalMilliseconds:F1} ms)");
    }
    catch (Exception exception)
    {
        stopwatch.Stop();
        failed++;
        Console.WriteLine($"FAIL  {name} ({stopwatch.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine("      " + exception.Message);
    }
}

suite.Stop();
Console.WriteLine();
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
Console.WriteLine($"MEASURE monitoring_suite tests={tests.Count} failed={failed} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static Task SystemCpuMathIsBoundedAsync()
{
    double? usage = TelemetryMath.CalculateSystemUsage(100, 500, 300, 140, 620, 380);
    Near(80, usage ?? -1, 0.001);
    return Task.CompletedTask;
}

static Task SystemCpuMathRejectsRegressionAsync()
{
    True(TelemetryMath.CalculateSystemUsage(100, 500, 300, 99, 620, 380) is null, "Regressing counters must be unavailable.");
    True(TelemetryMath.CalculateSystemUsage(100, 500, 300, 100, 500, 300) is null, "Zero elapsed ticks must be unavailable.");
    return Task.CompletedTask;
}

static Task ProcessCpuMathRespectsCapacityAsync()
{
    double usage = TelemetryMath.CalculateProcessUsage(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(1),
        processorCount: 4);
    Near(25, usage, 0.001);
    Equal(0d, TelemetryMath.CalculateProcessUsage(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4));
    return Task.CompletedTask;
}

static Task ProcessNamesAreSanitizedAsync()
{
    Equal("calc.exe", TelemetryMath.SanitizeProcessName(" calc\r\n.exe "));
    string longName = new('A', SystemTelemetryProvider.MaximumProcessNameLength + 20);
    Equal(SystemTelemetryProvider.MaximumProcessNameLength, TelemetryMath.SanitizeProcessName(longName).Length);
    Equal("Unavailable", TelemetryMath.SanitizeProcessName("\r\n"));
    return Task.CompletedTask;
}

static Task TelemetryHistoryValidatesInputAsync()
{
    Throws<ArgumentOutOfRangeException>(() => _ = new BoundedTelemetryHistory(1));
    Throws<ArgumentOutOfRangeException>(() => _ = new BoundedTelemetryHistory(BoundedTelemetryHistory.MaximumCapacity + 1));
    BoundedTelemetryHistory history = new(4);
    Throws<ArgumentOutOfRangeException>(() => history.Add(double.NaN));
    Throws<ArgumentOutOfRangeException>(() => history.Add(double.PositiveInfinity));
    return Task.CompletedTask;
}

static Task TelemetryHistoryIsBoundedAsync()
{
    BoundedTelemetryHistory history = new(3);
    var firstSnapshot = history.Add(10);
    _ = history.Add(20);
    _ = history.Add(30);
    var bounded = history.Add(40);
    Equal(1, firstSnapshot.Count);
    Equal(10d, firstSnapshot[0]);
    Equal(3, bounded.Count);
    Equal("20|30|40", string.Join('|', bounded));
    return Task.CompletedTask;
}

static Task TelemetryHistoryClampsAsync()
{
    BoundedTelemetryHistory history = new(3);
    _ = history.Add(-4);
    var snapshot = history.Add(104);
    Equal(0d, snapshot[0]);
    Equal(100d, snapshot[1]);
    return Task.CompletedTask;
}

static Task SampleWindowsAreBoundedAsync()
{
    Throws<ArgumentOutOfRangeException>(() => SystemTelemetryProvider.CaptureAsync(TimeSpan.FromMilliseconds(20)).GetAwaiter().GetResult());
    Throws<ArgumentOutOfRangeException>(() => SystemTelemetryProvider.CaptureAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult());
    return Task.CompletedTask;
}

static async Task PendingSampleCanBeCancelledAsync()
{
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => SystemTelemetryProvider.CaptureAsync(cancellationToken: cancellation.Token));
}

static async Task LiveCaptureIsBoundedAsync()
{
    SystemTelemetrySnapshot snapshot = await CaptureAsync();
    True(snapshot.CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1), "Capture timestamp is stale.");
    True(snapshot.CaptureDuration < TimeSpan.FromSeconds(5), "Capture exceeded five seconds.");
    True(snapshot.Processes.Count <= SystemTelemetryProvider.MaximumProcessCount, "Process result exceeded its bound.");
    True(snapshot.Volumes.Count <= SystemTelemetryProvider.MaximumVolumeCount, "Volume result exceeded its bound.");
    True(snapshot.Provenance.Contains("GetSystemTimes", StringComparison.Ordinal), "System timing provenance is missing.");
    True(snapshot.Limitations.Count >= 2, "Unsupported GPU/network signals must remain explicit.");
}

static async Task LiveProcessRowsExposeNoPathsAsync()
{
    SystemTelemetrySnapshot snapshot = await CaptureAsync();
    foreach (ProcessTelemetry process in snapshot.Processes)
    {
        True(process.ProcessId > 0, "Process ID must be positive.");
        True(process.Name.Length is > 0 and <= SystemTelemetryProvider.MaximumProcessNameLength, "Process name is outside bounds.");
        True(!process.Name.Any(char.IsControl), "Process name contains a control character.");
        True(!process.Name.Contains('\\') && !process.Name.Contains('/'), "Process rows must not expose executable paths.");
        True(process.CpuPercent is >= 0 and <= 100, "Process CPU percentage is outside bounds.");
        True(process.WorkingSetBytes >= 0, "Working set cannot be negative.");
    }
}

static async Task LivePercentagesAreBoundedAsync()
{
    SystemTelemetrySnapshot snapshot = await CaptureAsync();
    if (snapshot.CpuPercent is double cpu)
    {
        True(cpu is >= 0 and <= 100, "CPU percentage is outside bounds.");
    }

    if (snapshot.Memory is MemoryTelemetry memory)
    {
        True(memory.TotalBytes > 0, "Physical memory total must be positive.");
        True(memory.AvailableBytes <= memory.TotalBytes, "Available memory exceeds total memory.");
        True(memory.UsedPercent is >= 0 and <= 100, "Memory percentage is outside bounds.");
    }

    foreach (StorageVolumeTelemetry volume in snapshot.Volumes)
    {
        True(volume.TotalBytes > 0, "Volume total must be positive.");
        True(volume.AvailableBytes is >= 0 && volume.AvailableBytes <= volume.TotalBytes, "Volume availability is outside bounds.");
        True(volume.UsedPercent is >= 0 and <= 100, "Volume percentage is outside bounds.");
    }
}

static async Task CapturePerformanceIsMeasuredAsync()
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    SystemTelemetrySnapshot snapshot = await CaptureAsync();
    stopwatch.Stop();
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Monitoring capture exceeded its five-second test ceiling.");
    Console.WriteLine(
        $"      state={snapshot.State}; processes={snapshot.Processes.Count}; volumes={snapshot.Volumes.Count}; " +
        $"inaccessible={snapshot.InaccessibleProcessCount}; provider_ms={snapshot.CaptureDuration.TotalMilliseconds:F1}; wall_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
}

static async Task<SystemTelemetrySnapshot> CaptureAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("The live monitoring suite requires Windows.");
    }

    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
    return await SystemTelemetryProvider.CaptureAsync(TimeSpan.FromMilliseconds(150), timeout.Token);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Near(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected:F3} ± {tolerance:F3}, got {actual:F3}.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
