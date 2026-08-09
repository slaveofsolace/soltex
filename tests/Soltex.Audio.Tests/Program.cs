using System.Diagnostics;
using Soltex.Audio;

List<(string Name, Func<Task> Test)> tests =
[
    ("Endpoint names are sanitized and length-bounded", NameSanitizationIsBoundedAsync),
    ("Volume scalar is clamped to a 0-100 percent range", VolumePercentIsClampedAsync),
    ("State classification reports unavailable when enumeration fails", ClassificationReportsUnavailableAsync),
    ("State classification reports current with no endpoints and no failures", ClassificationReportsCurrentWithNoEndpointsAsync),
    ("State classification reports partial when some endpoints are inaccessible", ClassificationReportsPartialAsync),
    ("State classification reports current when fully observed", ClassificationReportsCurrentAsync),
    ("Cancellation stops a pending capture", PendingCaptureCanBeCancelledAsync),
    ("Live Core Audio capture is bounded and provenance-labeled", LiveCaptureIsBoundedAsync),
    ("Live endpoint names expose no control characters", LiveEndpointNamesAreSanitizedAsync),
    ("Live capture overhead is measured", CapturePerformanceIsMeasuredAsync)
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
Console.WriteLine($"MEASURE audio_suite tests={tests.Count} failed={failed} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static Task NameSanitizationIsBoundedAsync()
{
    Equal("Speakers (Realtek)", AudioEndpointProvider.SanitizeName(" Speakers\r\n (Realtek) "));
    string longName = new('A', AudioEndpointProvider.MaximumEndpointNameLength + 20);
    Equal(AudioEndpointProvider.MaximumEndpointNameLength, AudioEndpointProvider.SanitizeName(longName).Length);
    Equal("Unavailable", AudioEndpointProvider.SanitizeName(null));
    Equal("Unavailable", AudioEndpointProvider.SanitizeName("\r\n"));
    Equal("Unavailable", AudioEndpointProvider.SanitizeName("   "));
    return Task.CompletedTask;
}

static Task VolumePercentIsClampedAsync()
{
    AudioEndpoint below = new("Test", AudioEndpointDirection.Render, AudioEndpointState.Active, false, -0.5, false);
    AudioEndpoint above = new("Test", AudioEndpointDirection.Render, AudioEndpointState.Active, false, 1.5, false);
    AudioEndpoint mid = new("Test", AudioEndpointDirection.Render, AudioEndpointState.Active, false, 0.42, false);
    AudioEndpoint unknown = new("Test", AudioEndpointDirection.Render, AudioEndpointState.Active, false, null, null);
    Equal(0d, below.VolumePercent ?? -1);
    Equal(100d, above.VolumePercent ?? -1);
    Near(42, mid.VolumePercent ?? -1, 0.001);
    True(unknown.VolumePercent is null, "An unread volume must stay null rather than synthesized.");
    return Task.CompletedTask;
}

static Task ClassificationReportsUnavailableAsync()
{
    Equal(AudioObservationState.Unavailable, AudioEndpointProvider.ClassifyState(false, 0, 0));
    Equal(AudioObservationState.Unavailable, AudioEndpointProvider.ClassifyState(true, 0, 3));
    return Task.CompletedTask;
}

static Task ClassificationReportsCurrentWithNoEndpointsAsync()
{
    Equal(AudioObservationState.Current, AudioEndpointProvider.ClassifyState(true, 0, 0));
    return Task.CompletedTask;
}

static Task ClassificationReportsPartialAsync()
{
    Equal(AudioObservationState.Partial, AudioEndpointProvider.ClassifyState(true, 5, 2));
    return Task.CompletedTask;
}

static Task ClassificationReportsCurrentAsync()
{
    Equal(AudioObservationState.Current, AudioEndpointProvider.ClassifyState(true, 5, 0));
    return Task.CompletedTask;
}

static async Task PendingCaptureCanBeCancelledAsync()
{
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => AudioEndpointProvider.CaptureAsync(cancellation.Token));
}

static async Task LiveCaptureIsBoundedAsync()
{
    AudioEndpointSnapshot snapshot = await CaptureAsync();
    True(snapshot.CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1), "Capture timestamp is stale.");
    True(snapshot.CaptureDuration < TimeSpan.FromSeconds(5), "Capture exceeded five seconds.");
    True(snapshot.Render.Count() <= AudioEndpointProvider.MaximumEndpointCount, "Render endpoint result exceeded its bound.");
    True(snapshot.Capture.Count() <= AudioEndpointProvider.MaximumEndpointCount, "Capture endpoint result exceeded its bound.");
    True(snapshot.Provenance.Contains("IMMDeviceEnumerator", StringComparison.Ordinal), "Provenance is missing IMMDeviceEnumerator.");
    True(
        snapshot.Limitations.Any(item => item.Contains("does not set volume", StringComparison.Ordinal)),
        "The read-only scope must remain an explicit limitation.");
    foreach (AudioEndpoint endpoint in snapshot.Endpoints)
    {
        True(endpoint.VolumePercent is null or (>= 0 and <= 100), "Volume percentage is outside bounds.");
    }
}

static async Task LiveEndpointNamesAreSanitizedAsync()
{
    AudioEndpointSnapshot snapshot = await CaptureAsync();
    foreach (AudioEndpoint endpoint in snapshot.Endpoints)
    {
        True(endpoint.Name.Length is > 0 and <= AudioEndpointProvider.MaximumEndpointNameLength, "Endpoint name is outside bounds.");
        True(!endpoint.Name.Any(char.IsControl), "Endpoint name contains a control character.");
    }
}

static async Task CapturePerformanceIsMeasuredAsync()
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    AudioEndpointSnapshot snapshot = await CaptureAsync();
    stopwatch.Stop();
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Audio capture exceeded its five-second test ceiling.");
    Console.WriteLine(
        $"      state={snapshot.State}; render={snapshot.Render.Count()}; capture={snapshot.Capture.Count()}; " +
        $"inaccessible={snapshot.InaccessibleEndpointCount}; provider_ms={snapshot.CaptureDuration.TotalMilliseconds:F1}; " +
        $"wall_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
}

static async Task<AudioEndpointSnapshot> CaptureAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("The live audio suite requires Windows.");
    }

    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
    return await AudioEndpointProvider.CaptureAsync(timeout.Token);
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
