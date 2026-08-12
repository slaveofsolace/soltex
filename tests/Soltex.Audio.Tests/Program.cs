using System.Diagnostics;
using System.Runtime.InteropServices;
using Soltex.Audio;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

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
    ("Live endpoint capture overhead is measured", CapturePerformanceIsMeasuredAsync),
    ("Session names are sanitized and identity stays non-public", SessionNamesAndIdentityAreBoundedAsync),
    ("Core Audio session COM layout matches the Windows SDK", CoreAudioSessionLayoutMatchesSdkAsync),
    ("Session state classification preserves partial truth", SessionStateClassificationIsTruthfulAsync),
    ("Session mutation rejects unsafe or invalid requests", SessionMutationRejectsUnsafeRequestsAsync),
    ("Session volume success requires matching read-back", SessionVolumeRequiresReadBackAsync),
    ("Session mute success requires matching read-back", SessionMuteRequiresReadBackAsync),
    ("Session mutation exposes target drift without retrying", SessionMutationReportsTargetDriftAsync),
    ("Session mutation cancellation prevents backend dispatch", SessionMutationCancellationIsBoundedAsync),
    ("Live Core Audio session capture is bounded and path-free", LiveSessionCaptureIsBoundedAsync),
    ("Live session capture overhead is measured", SessionCapturePerformanceIsMeasuredAsync)
];

if (string.Equals(
        Environment.GetEnvironmentVariable("SOLTEX_RUN_AUDIO_WRITE_TEST"),
        "1",
        StringComparison.Ordinal))
{
    tests.Add((
        "Controlled silent session verifies live volume and mute read-back",
        ControlledSilentSessionVerifiesLiveReadBackAsync));
}

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

static Task SessionNamesAndIdentityAreBoundedAsync()
{
    Equal("Browser audio", AudioSessionProvider.SanitizeName("  Browser\r\n audio  ", 80, "Fallback"));
    Equal("Fallback", AudioSessionProvider.SanitizeName("\r\n", 80, "Fallback"));
    Equal("Fallback", AudioSessionProvider.SanitizeName("C:\\private\\player.exe", 80, "Fallback"));
    Equal("Fallback", AudioSessionProvider.SanitizeName("\\\\server\\share\\audio", 80, "Fallback"));
    string longName = new('S', AudioSessionProvider.MaximumSessionNameLength + 30);
    Equal(
        AudioSessionProvider.MaximumSessionNameLength,
        AudioSessionProvider.SanitizeName(
            longName,
            AudioSessionProvider.MaximumSessionNameLength,
            "Fallback").Length);

    string[] publicProperties = typeof(AudioSession)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Select(property => property.Name)
        .ToArray();
    True(!publicProperties.Contains("Identity", StringComparer.Ordinal),
        "The one-way session identity became part of the public model.");
    True(!publicProperties.Contains("ProcessId", StringComparer.Ordinal),
        "The process ID became part of the public session surface.");
    return Task.CompletedTask;
}

static Task CoreAudioSessionLayoutMatchesSdkAsync()
{
    Equal(new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), typeof(IAudioSessionControl).GUID);
    Equal(new Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), typeof(IAudioSessionControl2).GUID);
    Equal(new Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), typeof(IAudioSessionEnumerator).GUID);
    Equal(new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), typeof(IAudioSessionManager2).GUID);
    Equal(new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), typeof(ISimpleAudioVolume).GUID);
    Equal(
        "GetState|GetDisplayName|SetDisplayName|GetIconPath|SetIconPath|GetGroupingParam|SetGroupingParam|" +
        "RegisterAudioSessionNotification|UnregisterAudioSessionNotification|GetSessionIdentifier|" +
        "GetSessionInstanceIdentifier|GetProcessId|IsSystemSoundsSession|SetDuckingPreference",
        DeclaredMethodOrder(typeof(IAudioSessionControl2)));
    Equal(
        "GetAudioSessionControl|GetSimpleAudioVolume|GetSessionEnumerator|RegisterSessionNotification|" +
        "UnregisterSessionNotification|RegisterDuckNotification|UnregisterDuckNotification",
        DeclaredMethodOrder(typeof(IAudioSessionManager2)));
    Equal("SetMasterVolume|GetMasterVolume|SetMute|GetMute", DeclaredMethodOrder(typeof(ISimpleAudioVolume)));
    return Task.CompletedTask;
}

static string DeclaredMethodOrder(Type type) => string.Join(
    "|",
    type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
        .OrderBy(method => method.MetadataToken)
        .Select(method => method.Name));

static Task SessionStateClassificationIsTruthfulAsync()
{
    Equal(AudioObservationState.Unavailable, AudioSessionProvider.ClassifyState(false, 0, 0));
    Equal(AudioObservationState.Current, AudioSessionProvider.ClassifyState(true, 0, 0));
    Equal(AudioObservationState.Partial, AudioSessionProvider.ClassifyState(true, 1, 0));
    Equal(AudioObservationState.Partial, AudioSessionProvider.ClassifyState(true, 0, 1));
    return Task.CompletedTask;
}

static async Task SessionMutationRejectsUnsafeRequestsAsync()
{
    FakeMutationBackend backend = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.5,
        false));
    AudioSession controllable = CreateSession(canControl: true);
    AudioSessionMutationResult invalid = await AudioSessionController.ExecuteAsync(
        controllable,
        AudioSessionMutationKind.Volume,
        requestedVolumePercent: 101,
        requestedMute: null,
        backend);
    Equal(AudioSessionMutationStatus.Rejected, invalid.Status);
    Equal(0, backend.CallCount);

    AudioSession readOnly = CreateSession(canControl: false);
    AudioSessionMutationResult unavailable = await AudioSessionController.ExecuteAsync(
        readOnly,
        AudioSessionMutationKind.Mute,
        requestedVolumePercent: null,
        requestedMute: true,
        backend);
    Equal(AudioSessionMutationStatus.Rejected, unavailable.Status);
    Equal(0, backend.CallCount);
}

static async Task SessionVolumeRequiresReadBackAsync()
{
    FakeMutationBackend matching = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.37,
        false));
    AudioSessionMutationResult applied = await AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Volume,
        requestedVolumePercent: 37,
        requestedMute: null,
        matching);
    Equal(AudioSessionMutationStatus.Applied, applied.Status);
    Near(37, applied.ObservedVolumePercent ?? -1, 0.001);
    Near(0.37, matching.RequestedVolumeScalar ?? -1, 0.001);
    True(applied.Message.Contains("verified", StringComparison.OrdinalIgnoreCase),
        "The success message did not disclose read-back verification.");

    FakeMutationBackend mismatch = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.42,
        false));
    AudioSessionMutationResult notConfirmed = await AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Volume,
        requestedVolumePercent: 37,
        requestedMute: null,
        mismatch);
    Equal(AudioSessionMutationStatus.ReadBackMismatch, notConfirmed.Status);
    True(!notConfirmed.Succeeded, "A mismatched read-back was presented as success.");
}

static async Task SessionMuteRequiresReadBackAsync()
{
    FakeMutationBackend matching = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.6,
        true));
    AudioSessionMutationResult applied = await AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Mute,
        requestedVolumePercent: null,
        requestedMute: true,
        matching);
    Equal(AudioSessionMutationStatus.Applied, applied.Status);
    Equal(true, applied.ObservedMute);

    FakeMutationBackend mismatch = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.6,
        false));
    AudioSessionMutationResult notConfirmed = await AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Mute,
        requestedVolumePercent: null,
        requestedMute: true,
        mismatch);
    Equal(AudioSessionMutationStatus.ReadBackMismatch, notConfirmed.Status);
}

static async Task SessionMutationReportsTargetDriftAsync()
{
    FakeMutationBackend backend = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.TargetChanged,
        null,
        null));
    AudioSessionMutationResult result = await AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Mute,
        requestedVolumePercent: null,
        requestedMute: true,
        backend);
    Equal(AudioSessionMutationStatus.TargetChanged, result.Status);
    Equal(1, backend.CallCount);
    True(result.Message.Contains("nothing was changed", StringComparison.OrdinalIgnoreCase),
        "Target drift did not produce a non-mutation message.");
}

static async Task SessionMutationCancellationIsBoundedAsync()
{
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();
    FakeMutationBackend backend = new(new AudioSessionMutationBackendResult(
        AudioSessionMutationStatus.Applied,
        0.5,
        false));
    await ThrowsAsync<OperationCanceledException>(() => AudioSessionController.ExecuteAsync(
        CreateSession(canControl: true),
        AudioSessionMutationKind.Volume,
        requestedVolumePercent: 50,
        requestedMute: null,
        backend,
        cancellation.Token));
    Equal(0, backend.CallCount);
}

static async Task LiveSessionCaptureIsBoundedAsync()
{
    AudioSessionSnapshot snapshot = await CaptureSessionsAsync();
    True(snapshot.CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1), "Session capture timestamp is stale.");
    True(snapshot.CaptureDuration < TimeSpan.FromSeconds(5), "Session capture exceeded five seconds.");
    True(snapshot.Sessions.Count <= AudioSessionProvider.MaximumSessionCount,
        "Session result exceeded its public bound.");
    True(snapshot.ObservedSessionCount <= AudioSessionProvider.MaximumObservedSessionCount,
        "Session observation exceeded its internal bound.");
    True(snapshot.Provenance.Contains("IAudioSessionManager2", StringComparison.Ordinal),
        "Session provenance is missing IAudioSessionManager2.");
    True(snapshot.Limitations.Any(item => item.Contains("routing", StringComparison.OrdinalIgnoreCase)),
        "Session limitations omit the routing nonclaim.");
    foreach (AudioSession session in snapshot.Sessions)
    {
        True(session.Name.Length is > 0 and <= AudioSessionProvider.MaximumSessionNameLength,
            "Session name is outside bounds.");
        True(session.EndpointName.Length is > 0 and <= AudioSessionProvider.MaximumEndpointNameLength,
            "Session endpoint name is outside bounds.");
        True(!session.Name.Any(char.IsControl) && !session.EndpointName.Any(char.IsControl),
            "Session output contains a control character.");
        True(session.VolumePercent is >= 0 and <= 100, "Session volume is outside bounds.");
        True(!session.Name.Contains(":\\", StringComparison.Ordinal),
            "Session name exposed a drive-qualified path.");
    }
}

static async Task SessionCapturePerformanceIsMeasuredAsync()
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    AudioSessionSnapshot snapshot = await CaptureSessionsAsync();
    stopwatch.Stop();
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Session capture exceeded its five-second test ceiling.");
    Console.WriteLine(
        $"      state={snapshot.State}; exposed={snapshot.Sessions.Count}; observed={snapshot.ObservedSessionCount}; " +
        $"inaccessible={snapshot.InaccessibleSessionCount}; omitted={snapshot.OmittedSessionCount}; " +
        $"provider_ms={snapshot.CaptureDuration.TotalMilliseconds:F1}; wall_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
}

static async Task ControlledSilentSessionVerifiesLiveReadBackAsync()
{
    string wavePath = Path.Combine(
        Path.GetTempPath(),
        $"soltex-audio-session-{Guid.NewGuid():N}.wav");
    ControlledAudioFixture.WriteSilentWave(wavePath);
    AudioSession? ownedSession = null;
    try
    {
        True(ControlledAudioFixture.StartLoop(wavePath),
            "The controlled silent WinMM session could not be started.");
        uint processId = checked((uint)Environment.ProcessId);
        Stopwatch wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(4))
        {
            AudioSessionSnapshot snapshot = await CaptureSessionsAsync();
            ownedSession = snapshot.Sessions.FirstOrDefault(session =>
                session.Identity?.ProcessId == processId && session.CanControl);
            if (ownedSession is not null)
            {
                break;
            }

            await Task.Delay(100);
        }

        True(ownedSession is not null,
            "The controlled silent playback session did not appear within four seconds.");

        AudioSessionMutationResult volume = await AudioSessionController.SetVolumeAsync(
            ownedSession!,
            ownedSession!.VolumePercent);
        Equal(AudioSessionMutationStatus.Applied, volume.Status);
        Near(ownedSession.VolumePercent, volume.ObservedVolumePercent ?? -1, 0.5);

        AudioSessionMutationResult mute = await AudioSessionController.SetMuteAsync(
            ownedSession,
            ownedSession.IsMuted);
        Equal(AudioSessionMutationStatus.Applied, mute.Status);
        Equal<bool?>(ownedSession.IsMuted, mute.ObservedMute);
    }
    finally
    {
        ControlledAudioFixture.Stop();
        try
        {
            File.Delete(wavePath);
        }
        catch (IOException)
        {
            // The fixture already stopped; a transient temp-file lock is not a product mutation.
        }
        catch (UnauthorizedAccessException)
        {
            // The fixture already stopped; a transient temp-file lock is not a product mutation.
        }
    }
}

static AudioSession CreateSession(bool canControl) => new(
    "Test audio app",
    "Test playback device",
    0.5,
    isMuted: false,
    canControl,
    canControl ? "Volume and mute available" : "Read-only test session",
    canControl
        ? new AudioSessionIdentity("endpoint-hash", "session-hash", 42, 12345)
        : null);

static async Task<AudioEndpointSnapshot> CaptureAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("The live audio suite requires Windows.");
    }

    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
    return await AudioEndpointProvider.CaptureAsync(timeout.Token);
}

static async Task<AudioSessionSnapshot> CaptureSessionsAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("The live audio-session suite requires Windows.");
    }

    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
    return await AudioSessionProvider.CaptureAsync(timeout.Token);
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

internal sealed class FakeMutationBackend(AudioSessionMutationBackendResult result)
    : IAudioSessionMutationBackend
{
    internal int CallCount { get; private set; }

    internal double? RequestedVolumeScalar { get; private set; }

    internal bool? RequestedMute { get; private set; }

    public AudioSessionMutationBackendResult Apply(
        AudioSessionIdentity identity,
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        RequestedVolumeScalar = requestedVolumeScalar;
        RequestedMute = requestedMute;
        return result;
    }
}

internal static class ControlledAudioFixture
{
    private const uint SoundAsync = 0x0001;
    private const uint SoundNoDefault = 0x0002;
    private const uint SoundLoop = 0x0008;
    private const uint SoundFileName = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(
        string? sound,
        IntPtr module,
        uint flags);

    internal static bool StartLoop(string wavePath) =>
        PlaySound(wavePath, IntPtr.Zero, SoundAsync | SoundNoDefault | SoundLoop | SoundFileName);

    internal static void Stop() =>
        _ = PlaySound(null, IntPtr.Zero, 0);

    internal static void WriteSilentWave(string path)
    {
        const int sampleRate = 8_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int seconds = 1;
        int dataLength = sampleRate * channels * (bitsPerSample / 8) * seconds;
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }
}
