using System.Diagnostics;
using Soltex.Capture;

List<(string Name, Action Test)> tests =
[
    ("Default profile is conservative and normalized", DefaultProfileIsConservative),
    ("Unknown profile values repair to safe defaults", UnknownProfileValuesRepairSafely),
    ("Replay defaults off and clamps both hard bounds", ReplayPolicyIsFailClosedAndBounded),
    ("Regions normalize to the platform geometry bound", RegionsAreBounded),
    ("Session lifecycle requires explicit source selection", SessionRequiresExplicitSelection),
    ("Screenshot completion is a one-shot non-recording state", ScreenshotCompletesWithoutRecording),
    ("Invalid session transitions are rejected", InvalidTransitionsAreRejected),
    ("Active and paused capture require a visible indicator", VisibleIndicatorIsMandatory),
    ("Cancellation remains available through stopping", CancellationRemainsAvailable),
    ("Fault details are bounded and normalized", FaultDetailsAreBounded),
    ("Clip manifests remove path-like names and invalid hashes", ManifestsFailClosed),
    ("Owned capture paths cannot escape the configured root", OwnedPathsRemainContained),
    ("Capability snapshots are bounded and queryable", CapabilitiesAreBounded),
    ("Windows capability probing remains truthful and bounded", WindowsCapabilityProbeIsBounded),
    ("Screenshot dimensions reject empty and oversized frames", ScreenshotDimensionsAreBounded),
    ("Screenshot cancellation is honored before consent UI", ScreenshotCancellationPrecedesConsent)
];

int failed = 0;
Stopwatch suite = Stopwatch.StartNew();
foreach ((string name, Action test) in tests)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        test();
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
Console.WriteLine($"MEASURE capture_contract_suite tests={tests.Count} failed={failed} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static void DefaultProfileIsConservative()
{
    CaptureProfile profile = CaptureProfile.Default.Normalize();
    Equal(CaptureAudioMode.None, profile.AudioMode);
    Equal(CaptureEncoderPreference.HardwarePreferred, profile.EncoderPreference);
    Equal(60, profile.FrameRate);
    True(!profile.Replay.Enabled, "Replay must remain disabled by default.");
}

static void UnknownProfileValuesRepairSafely()
{
    CaptureProfile profile = new(
        -3,
        (CaptureSourceKind)99,
        (CaptureAudioMode)99,
        (CaptureEncoderPreference)99,
        -1,
        int.MaxValue,
        false,
        new ReplayBufferPolicy(true, int.MaxValue, long.MaxValue));
    CaptureProfile normalized = profile.Normalize();
    Equal(CaptureProfile.CurrentContractVersion, normalized.ContractVersion);
    Equal(CaptureSourceKind.Display, normalized.SourceKind);
    Equal(CaptureAudioMode.None, normalized.AudioMode);
    Equal(CaptureEncoderPreference.HardwarePreferred, normalized.EncoderPreference);
    Equal(CaptureProfile.MinimumFrameRate, normalized.FrameRate);
    Equal(CaptureProfile.MaximumVideoBitrate, normalized.VideoBitrate);
}

static void ReplayPolicyIsFailClosedAndBounded()
{
    True(!ReplayBufferPolicy.Disabled.Enabled, "Replay default unexpectedly enabled.");
    ReplayBufferPolicy normalized = new ReplayBufferPolicy(true, 9_999, long.MaxValue).Normalize();
    Equal(ReplayBufferPolicy.MaximumDurationSeconds, normalized.DurationSeconds);
    Equal(ReplayBufferPolicy.AbsoluteMaximumBytes, normalized.MaximumBytes);
    ReplayBufferPolicy minimum = new ReplayBufferPolicy(true, 0, 0).Normalize();
    Equal(ReplayBufferPolicy.MinimumDurationSeconds, minimum.DurationSeconds);
    Equal(ReplayBufferPolicy.MinimumMaximumBytes, minimum.MaximumBytes);
}

static void RegionsAreBounded()
{
    CaptureRegion region = new CaptureRegion(
        int.MinValue,
        int.MaxValue,
        int.MaxValue,
        -4).Normalize();
    Equal(-CaptureRegion.MaximumDimension, region.X);
    Equal(CaptureRegion.MaximumDimension, region.Y);
    Equal(CaptureRegion.MaximumDimension, region.Width);
    Equal(0, region.Height);
    True(region.IsEmpty, "A zero-height region must be empty.");
}

static void SessionRequiresExplicitSelection()
{
    CaptureSessionController controller = new();
    Equal(CaptureSessionPhase.Idle, controller.Snapshot.Phase);
    controller.BeginSelection();
    CaptureSessionState ready = controller.SourceSelected("Display 1");
    Equal(CaptureSessionPhase.Ready, ready.Phase);
    CaptureSessionState recording = controller.Start(DateTimeOffset.UtcNow);
    Equal(CaptureSessionPhase.Recording, recording.Phase);
}

static void ScreenshotCompletesWithoutRecording()
{
    CaptureSessionController controller = new();
    controller.BeginSelection();
    CaptureSessionState state = controller.CompleteScreenshot("Display 1", DateTimeOffset.UtcNow);
    Equal(CaptureSessionPhase.Completed, state.Phase);
    True(!state.RequiresVisibleIndicator, "A completed one-shot screenshot retained an active indicator.");
    Equal("Display 1", state.SourceLabel);
}

static void InvalidTransitionsAreRejected()
{
    CaptureSessionController controller = new();
    Throws<InvalidOperationException>(() => controller.Start(DateTimeOffset.UtcNow));
    controller.BeginSelection();
    Throws<InvalidOperationException>(() => controller.Pause());
    controller.SourceSelected("Window");
    Throws<InvalidOperationException>(() => controller.Complete(DateTimeOffset.UtcNow));
}

static void VisibleIndicatorIsMandatory()
{
    CaptureSessionController controller = ReadyController();
    True(controller.Start(DateTimeOffset.UtcNow).RequiresVisibleIndicator,
        "Recording did not require an indicator.");
    True(controller.Pause().RequiresVisibleIndicator,
        "Paused capture did not require an indicator.");
    True(controller.BeginStopping().RequiresVisibleIndicator,
        "Stopping capture did not require an indicator.");
    True(!controller.Complete(DateTimeOffset.UtcNow).RequiresVisibleIndicator,
        "Completed capture still required an indicator.");
}

static void CancellationRemainsAvailable()
{
    CaptureSessionController controller = ReadyController();
    controller.Start(DateTimeOffset.UtcNow);
    controller.BeginStopping();
    CaptureSessionState state = controller.Cancel();
    Equal(CaptureSessionPhase.Faulted, state.Phase);
    Equal(CaptureFailureKind.Cancelled, state.FailureKind);
}

static void FaultDetailsAreBounded()
{
    CaptureSessionController controller = ReadyController();
    controller.Start(DateTimeOffset.UtcNow);
    CaptureSessionState state = controller.Fail(
        CaptureFailureKind.DeviceLost,
        "  Device\r\n" + new string('x', 300));
    Equal(CaptureFailureKind.DeviceLost, state.FailureKind);
    True(state.Detail.Length <= 240, "Fault detail exceeded its bound.");
    True(!state.Detail.Any(char.IsControl), "Fault detail retained control characters.");
}

static void ManifestsFailClosed()
{
    ClipManifest manifest = new(
        Guid.Empty,
        CaptureOutputKind.Recording,
        default,
        TimeSpan.FromDays(4),
        int.MaxValue,
        -1,
        500,
        (CaptureAudioMode)99,
        "  hardware\r\n encoder  ",
        long.MaxValue,
        "not-a-hash",
        "C:\\private\\recording.mp4");
    ClipManifest normalized = manifest.Normalize();
    True(normalized.ClipId != Guid.Empty, "Manifest did not repair its identifier.");
    Equal("Recording.mp4", normalized.FileName);
    Equal(string.Empty, normalized.Sha256);
    Equal(CaptureStoragePolicy.MaximumClipBytes, normalized.FileBytes);
    Equal("hardware encoder", normalized.Encoder);
}

static void OwnedPathsRemainContained()
{
    string root = Path.Combine(Path.GetTempPath(), "SoltexCaptureTests", Guid.NewGuid().ToString("N"));
    Guid id = Guid.NewGuid();
    CaptureStoragePlan plan = CaptureStoragePolicy.PlanOwnedFile(
        root,
        CaptureOutputKind.Screenshot,
        new DateTimeOffset(2026, 8, 25, 12, 34, 56, TimeSpan.Zero),
        id);
    True(plan.FilePath.StartsWith(plan.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
        "Planned file escaped its root.");
    True(plan.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
        "Screenshot did not use PNG.");
    Throws<ArgumentException>(() => CaptureStoragePolicy.PlanOwnedFile(root, CaptureOutputKind.Recording, DateTimeOffset.UtcNow, Guid.Empty));
}

static void CapabilitiesAreBounded()
{
    CaptureCapability[] capabilities = Enumerable.Range(0, 24)
        .Select(index => new CaptureCapability($"cap-{index}", index == 2, index == 2, "Ready"))
        .ToArray();
    CaptureCapabilitySnapshot snapshot = new(
        DateTimeOffset.UtcNow,
        capabilities,
        "  Runtime\r\n probe required.  ");
    Equal(16, snapshot.Capabilities.Count);
    True(snapshot.IsAvailable("cap-2"), "Available capability was not found.");
    Equal("Runtime probe required.", snapshot.Limitation);
}

static void WindowsCapabilityProbeIsBounded()
{
    CaptureCapabilitySnapshot snapshot = WindowsGraphicsCaptureScreenshotService.Probe();
    True(snapshot.CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1),
        "Capability timestamp is stale.");
    Equal(3, snapshot.Capabilities.Count);
    CaptureCapability screenshot = snapshot.Capabilities.Single(item => item.Id == "screenshot");
    True(screenshot.RequiresConsent, "Screenshot capability did not preserve OS consent.");
    True(!snapshot.IsAvailable("recording"), "Recording became available without an encoder path.");
    True(!snapshot.IsAvailable("replay"), "Replay became available without bounded runtime proof.");
}

static void ScreenshotDimensionsAreBounded()
{
    Throws<CaptureScreenshotException>(() =>
        WindowsGraphicsCaptureScreenshotService.ValidateSize(0, 1));
    Throws<CaptureScreenshotException>(() =>
        WindowsGraphicsCaptureScreenshotService.ValidateSize(16_384, 16_384));
    Throws<CaptureScreenshotException>(() =>
        WindowsGraphicsCaptureScreenshotService.ValidateSize(16_385, 1));
    WindowsGraphicsCaptureScreenshotService.ValidateSize(16_384, 1);
    WindowsGraphicsCaptureScreenshotService.ValidateSize(7_680, 4_320);
}

static void ScreenshotCancellationPrecedesConsent()
{
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() =>
        WindowsGraphicsCaptureScreenshotService.CaptureAsync(
            new CaptureScreenshotRequest(1, Path.GetTempPath()),
            cancellation.Token).GetAwaiter().GetResult());
}

static CaptureSessionController ReadyController()
{
    CaptureSessionController controller = new();
    controller.BeginSelection();
    controller.SourceSelected("Display 1");
    return controller;
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}; received {actual}.");
    }
}

static void Throws<T>(Action action)
    where T : Exception
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
