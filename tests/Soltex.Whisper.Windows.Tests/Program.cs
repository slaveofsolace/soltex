using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Soltex.Whisper;
using Soltex.Whisper.Windows;

List<(string Name, Func<Task> Run)> tests =
[
    ("selected device is passed to the backend and fallback is reported", DeviceSelectionAndFallback),
    ("a disconnect reopens the default capture device once", DisconnectRecovery),
    ("enumeration cancellation is honored", EnumerationCancellation),
    ("open cancellation is honored", OpenCancellation),
    ("capture cancellation discards buffered audio", CaptureCancellation),
    ("the PCM accumulator rejects bytes beyond its bound", BufferBound),
    ("owned packet and clip audio are zeroed on disposal", AudioIsZeroed),
    ("stereo float WASAPI packets normalize to bounded 16 kHz mono", NativeFormatNormalization),
    ("metering is content-free and throttled", MeteringIsThrottled),
    ("capture failures map to distinct readiness states", ReadinessMapping),
    ("native HRESULTs map to stable capture categories", HResultMapping)
];

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_LIVE_CAPTURE"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host WASAPI capture returns disposable bounded audio", LiveCapture));
}

int failures = 0;
foreach ((string name, Func<Task> run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Whisper Windows capture tests passed.");
return failures == 0 ? 0 : 1;

static async Task DeviceSelectionAndFallback()
{
    FakeBackend backend = FakeBackend.WithOnePacket(
        new WhisperCaptureSelection("default-id", "Default microphone", UsedFallback: true));
    FakeFactory factory = new([backend]);
    await using WhisperWasapiCaptureSource source = new(factory, "missing-id");
    WhisperCaptureSelection? observed = null;
    source.DeviceSelectionChanged += (_, selection) => observed = selection;

    Task<WhisperAudioClip> capture = source.CaptureAsync(
        WhisperCaptureMode.PushToTalk,
        CancellationToken.None).AsTask();
    await backend.PacketServed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    True(source.CompleteCurrentCapture());

    using WhisperAudioClip clip = await capture.WaitAsync(TimeSpan.FromSeconds(2));
    Equal("missing-id", factory.RequestedDeviceIds.Single());
    True(observed?.UsedFallback == true);
    Equal(WhisperWasapiCaptureSource.OutputSampleRateHz, clip.SampleRateHz);
    Equal(1, clip.ChannelCount);
}

static async Task DisconnectRecovery()
{
    FakeBackend disconnected = FakeBackend.Disconnected(
        new WhisperCaptureSelection("selected-id", "Selected", UsedFallback: false));
    FakeBackend fallback = FakeBackend.WithOnePacket(
        new WhisperCaptureSelection("default-id", "Default", UsedFallback: true));
    FakeFactory factory = new([disconnected, fallback]);
    await using WhisperWasapiCaptureSource source = new(factory, "selected-id");

    Task<WhisperAudioClip> capture = source.CaptureAsync(
        WhisperCaptureMode.HandsFree,
        CancellationToken.None).AsTask();
    await fallback.PacketServed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    True(source.CompleteCurrentCapture());

    using WhisperAudioClip clip = await capture.WaitAsync(TimeSpan.FromSeconds(2));
    Equal(2, factory.RequestedDeviceIds.Count);
    Equal("selected-id", factory.RequestedDeviceIds[0]);
    Equal<string?>(null, factory.RequestedDeviceIds[1]);
    True(clip.Pcm16.Length > 0);
}

static async Task EnumerationCancellation()
{
    FakeFactory factory = new([]) { BlockEnumeration = true };
    await using WhisperWasapiCaptureSource source = new(factory);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(30));
    await ThrowsAsync<OperationCanceledException>(async () =>
        await source.EnumerateDevicesAsync(cancellation.Token));
}

static async Task OpenCancellation()
{
    FakeFactory factory = new([]) { BlockOpen = true };
    await using WhisperWasapiCaptureSource source = new(factory);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(30));
    await ThrowsAsync<OperationCanceledException>(async () =>
        await source.CaptureAsync(WhisperCaptureMode.PushToTalk, cancellation.Token));
}

static async Task CaptureCancellation()
{
    byte[] observedPacket = CreatePcmPacket();
    FakeBackend backend = FakeBackend.WithOnePacket(
        new WhisperCaptureSelection("id", "Microphone", UsedFallback: false),
        observedPacket);
    FakeFactory factory = new([backend]);
    await using WhisperWasapiCaptureSource source = new(factory);
    using CancellationTokenSource cancellation = new();
    Task<WhisperAudioClip> capture = source.CaptureAsync(
        WhisperCaptureMode.PushToTalk,
        cancellation.Token).AsTask();

    await backend.PacketServed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(async () => await capture);
    True(observedPacket.All(value => value == 0));
}

static Task BufferBound()
{
    using BoundedPcmAccumulator buffer = new(8);
    buffer.Append(new byte[8]);
    Throws<WhisperCaptureException>(() => buffer.Append(new byte[2]));
    return Task.CompletedTask;
}

static Task AudioIsZeroed()
{
    byte[] packetBytes = [1, 2, 3, 4];
    using (WhisperPcmPacket packet = new(packetBytes))
    {
        Equal(4, packet.Memory.Length);
    }

    True(packetBytes.All(value => value == 0));

    byte[] clipBytes = [5, 6, 7, 8];
    WhisperAudioClip clip = WhisperAudioClip.CreateOwned(
        clipBytes,
        16_000,
        1,
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 8_000));
    clip.Dispose();
    True(clipBytes.All(value => value == 0));
    Throws<ObjectDisposedException>(() => _ = clip.Pcm16);
    return Task.CompletedTask;
}

static Task NativeFormatNormalization()
{
    WaveFormatEx format = new()
    {
        FormatTag = 3,
        Channels = 2,
        SamplesPerSecond = 48_000,
        AverageBytesPerSecond = 384_000,
        BlockAlign = 8,
        BitsPerSample = 32,
        ExtraSize = 0
    };
    IntPtr formatPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());
    byte[] raw = new byte[480 * format.BlockAlign];
    IntPtr rawPointer = Marshal.AllocHGlobal(raw.Length);
    byte[]? normalized = null;
    try
    {
        Marshal.StructureToPtr(format, formatPointer, fDeleteOld: false);
        byte[] sample = BitConverter.GetBytes(0.25f);
        for (int offset = 0; offset < raw.Length; offset += sizeof(float))
        {
            sample.CopyTo(raw, offset);
        }

        Marshal.Copy(raw, 0, rawPointer, raw.Length);
        Pcm16MonoNormalizer normalizer = new(formatPointer);
        normalized = normalizer.Normalize(rawPointer, frameCount: 480, silent: false);
        Equal(320, normalized.Length);
        True(normalized.Any(value => value != 0));
        return Task.CompletedTask;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(raw);
        if (normalized is not null)
        {
            CryptographicOperations.ZeroMemory(normalized);
        }

        Marshal.FreeHGlobal(rawPointer);
        Marshal.FreeCoTaskMem(formatPointer);
    }
}

static async Task LiveCapture()
{
    await using WhisperWasapiCaptureSource source = new();
    WhisperCaptureDeviceSnapshot devices =
        await source.EnumerateDevicesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    WhisperCaptureDevice device = devices.Devices.FirstOrDefault(candidate => candidate.IsDefault)
        ?? devices.Devices.FirstOrDefault()
        ?? throw new InvalidOperationException("The owner host has no active capture device.");
    source.SelectInputDevice(device.Id);
    WhisperCaptureSelection? selection = null;
    source.DeviceSelectionChanged += (_, observed) => selection = observed;

    Stopwatch timer = Stopwatch.StartNew();
    Task<WhisperAudioClip> capture = source.CaptureAsync(
        WhisperCaptureMode.PushToTalk,
        CancellationToken.None).AsTask();
    await Task.Delay(TimeSpan.FromSeconds(1));
    True(source.CompleteCurrentCapture());
    using WhisperAudioClip clip = await capture.WaitAsync(TimeSpan.FromSeconds(5));
    timer.Stop();

    True(clip.Pcm16.Length is > 0 and <= WhisperWasapiCaptureSource.MaximumCaptureBytes);
    True(clip.ChannelCount == 1 &&
         clip.SampleRateHz == WhisperWasapiCaptureSource.OutputSampleRateHz);
    Console.WriteLine(
        $"MEASURE whisper_live_capture devices={devices.Devices.Count} " +
        $"bytes={clip.Pcm16.Length} duration_ms={clip.Duration.TotalMilliseconds:F1} " +
        $"wall_ms={timer.Elapsed.TotalMilliseconds:F1} fallback={selection?.UsedFallback == true}");
}

static async Task MeteringIsThrottled()
{
    FakeBackend backend = FakeBackend.WithPackets(
        new WhisperCaptureSelection("id", "Microphone", UsedFallback: false),
        Enumerable.Range(0, 8).Select(_ => CreatePcmPacket()).ToArray());
    FakeFactory factory = new([backend]);
    await using WhisperWasapiCaptureSource source = new(factory);
    List<double> levels = [];
    source.InputLevelChanged += (_, args) => levels.Add(args.Level);

    Task<WhisperAudioClip> capture = source.CaptureAsync(
        WhisperCaptureMode.PushToTalk,
        CancellationToken.None).AsTask();
    await backend.AllPacketsServed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    True(source.CompleteCurrentCapture());
    using WhisperAudioClip clip = await capture.WaitAsync(TimeSpan.FromSeconds(2));

    True(levels.Count is >= 2 and <= 3); // One live update plus the final zero.
    True(levels.All(level => level is >= 0 and <= 1));
    Equal(0d, levels[^1]);
}

static Task ReadinessMapping()
{
    WhisperReadinessInputs initial = new(
        FeatureEnabled: true,
        MicrophoneSelected: true,
        MicrophonePermissionGranted: true,
        ShortcutsRegistered: false,
        ShortcutRegistrationError: null,
        TranscriberConfigured: false,
        TranscriberCredentialAvailable: false,
        TargetInspectionAvailable: false,
        AutoSendEnabled: false,
        AutoSendWarningAccepted: false,
        EnabledAutoSendProfileCount: 0);

    WhisperReadinessInputs denied = WhisperCaptureReadiness.Apply(
        initial,
        new WhisperCaptureException(
            WhisperCaptureFailureKind.PermissionDenied,
            "untrusted detail"),
        hasSelectedDevice: true);
    False(denied.MicrophonePermissionGranted);

    WhisperReadinessInputs missing = WhisperCaptureReadiness.Apply(
        initial,
        new WhisperCaptureException(WhisperCaptureFailureKind.NoDevice, "missing"),
        hasSelectedDevice: false);
    False(missing.MicrophoneSelected);

    WhisperReadinessInputs busy = WhisperCaptureReadiness.Apply(
        initial,
        new WhisperCaptureException(WhisperCaptureFailureKind.DeviceInUse, "busy"),
        hasSelectedDevice: true);
    True(busy.MicrophoneError?.Contains("exclusive", StringComparison.Ordinal) == true);
    return Task.CompletedTask;
}

static Task HResultMapping()
{
    Equal(
        WhisperCaptureFailureKind.PermissionDenied,
        WindowsWasapiCaptureBackendFactory.MapException(
            WasapiNative.AccessDenied,
            "ignored").Kind);
    Equal(
        WhisperCaptureFailureKind.DeviceInUse,
        WindowsWasapiCaptureBackendFactory.MapException(
            WasapiNative.DeviceInUse,
            "ignored").Kind);
    Equal(
        WhisperCaptureFailureKind.DeviceDisconnected,
        WindowsWasapiCaptureBackendFactory.MapException(
            WasapiNative.DeviceInvalidated,
            "ignored").Kind);
    return Task.CompletedTask;
}

static byte[] CreatePcmPacket()
{
    byte[] packet = new byte[320];
    for (int index = 0; index < packet.Length; index += 2)
    {
        packet[index] = 0;
        packet[index + 1] = 32;
    }

    return packet;
}

static async Task ThrowsAsync<TException>(Func<Task> action)
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

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool value) => True(!value);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

internal sealed class FakeFactory : IWhisperCaptureBackendFactory
{
    private readonly ConcurrentQueue<FakeBackend> _backends;

    internal FakeFactory(IEnumerable<FakeBackend> backends)
    {
        _backends = new ConcurrentQueue<FakeBackend>(backends);
    }

    internal List<string?> RequestedDeviceIds { get; } = [];

    internal bool BlockEnumeration { get; init; }

    internal bool BlockOpen { get; init; }

    public async ValueTask<WhisperCaptureDeviceSnapshot> EnumerateAsync(
        CancellationToken cancellationToken)
    {
        if (BlockEnumeration)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return new WhisperCaptureDeviceSnapshot([]);
    }

    public async ValueTask<IWhisperCaptureBackend> OpenAsync(
        string? requestedDeviceId,
        CancellationToken cancellationToken)
    {
        RequestedDeviceIds.Add(requestedDeviceId);
        if (BlockOpen)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (!_backends.TryDequeue(out FakeBackend? backend))
        {
            throw new InvalidOperationException("No scripted backend remained.");
        }

        return backend;
    }
}

internal sealed class FakeBackend : IWhisperCaptureBackend
{
    private readonly ConcurrentQueue<byte[]> _packets;
    private readonly bool _disconnect;
    private int _servedCount;

    private FakeBackend(
        WhisperCaptureSelection selection,
        IEnumerable<byte[]> packets,
        bool disconnect)
    {
        Selection = selection;
        _packets = new ConcurrentQueue<byte[]>(packets);
        _disconnect = disconnect;
        int packetCount = _packets.Count;
        ExpectedPacketCount = packetCount;
        if (packetCount == 0)
        {
            AllPacketsServed.TrySetResult();
        }
    }

    public WhisperCaptureSelection Selection { get; }

    internal int ExpectedPacketCount { get; }

    internal TaskCompletionSource PacketServed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource AllPacketsServed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static FakeBackend WithOnePacket(
        WhisperCaptureSelection selection,
        byte[]? packet = null) =>
        new(selection, [packet ?? BuildPcmPacket()], disconnect: false);

    internal static FakeBackend WithPackets(
        WhisperCaptureSelection selection,
        params byte[][] packets) =>
        new(selection, packets, disconnect: false);

    internal static FakeBackend Disconnected(WhisperCaptureSelection selection) =>
        new(selection, [], disconnect: true);

    public async ValueTask<WhisperPcmPacket> ReadAsync(CancellationToken cancellationToken)
    {
        if (_disconnect)
        {
            throw new WhisperCaptureException(
                WhisperCaptureFailureKind.DeviceDisconnected,
                "Scripted disconnect.");
        }

        if (_packets.TryDequeue(out byte[]? packet))
        {
            int served = Interlocked.Increment(ref _servedCount);
            PacketServed.TrySetResult();
            if (served == ExpectedPacketCount)
            {
                AllPacketsServed.TrySetResult();
            }

            return new WhisperPcmPacket(packet);
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new UnreachableException();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static byte[] BuildPcmPacket()
    {
        byte[] packet = new byte[320];
        for (int index = 0; index < packet.Length; index += 2)
        {
            packet[index + 1] = 32;
        }

        return packet;
    }
}
