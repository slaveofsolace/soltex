using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Soltex.Security;
using Soltex.Whisper;
using Soltex.Whisper.Windows;
using Soltex.Whisper.Windows.Tests;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

if (args is ["--elevated-target-host", var elevatedMarker])
{
    return LiveElevatedTargetHost.RunChild(elevatedMarker);
}

if (args is ["--live-local-model"])
{
    await LiveLocalModel();
    Console.WriteLine("PASS owner-host local model completes content-free transcription proof");
    return 0;
}

if (args is ["--live-electron-target"])
{
    await LiveElectronTargetMatrix();
    Console.WriteLine("PASS owner-host Electron input verifies bounded submission");
    return 0;
}

if (args is ["--live-terminal-target"])
{
    await LiveWindowsTerminalTargetMatrix();
    Console.WriteLine("PASS owner-host Windows Terminal preserves the separate submit opt-in");
    return 0;
}

if (args is ["--live-elevated-target"])
{
    await LiveElevatedTargetMatrix();
    Console.WriteLine("PASS owner-host elevated editor fails closed before mutation");
    return 0;
}

if (args is ["--live-winui-target"])
{
    await LiveWinUiTargetMatrix();
    Console.WriteLine("PASS owner-host WinUI target verifies bounded submission");
    return 0;
}

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
    ("native HRESULTs map to stable capture categories", HResultMapping),
    ("shortcut chord matching is order independent and suppresses repeat", ShortcutChordMatching),
    ("shortcut mouse buttons preserve press and release", ShortcutMouseButtons),
    ("shortcut registration rejects unsupported Windows keys", UnsupportedShortcutKey),
    ("shortcut registration rolls back atomically after a replacement failure", AtomicShortcutRollback),
    ("target inspection classifies supported control categories", TargetCategories),
    ("target inspection exposes content-free pattern capabilities", TargetCapabilities),
    ("target inspection fails closed for protected read-only and unknown controls", TargetSafetyStates),
    ("a slow target provider times out without blocking the caller", TargetTimeout),
    ("a throwing target provider resolves to unavailable", TargetProviderFailure),
    ("target inspection cancellation is honored", TargetCancellation),
    ("direct target insertion precedes clipboard fallback", DirectInsertionPrecedesClipboard),
    ("clipboard paste restores only while ownership remains", ClipboardOwnershipRestore),
    ("clipboard restoration is skipped after another owner takes it", ClipboardOwnershipLost),
    ("focus drift after clipboard staging falls back to copy", DeliveryFocusDrift),
    ("an unknown target copies without emitting paste input", DeliveryUnknownTarget),
    ("a rejected paste leaves the transcript copied", DeliveryPasteRejected),
    ("cancellation before paste restores an owned clipboard", DeliveryCancellationRestores),
    ("verified Enter uses one untagged virtual-key press and release", VerifiedEnterInputEncoding),
    ("verified Enter refuses foreground drift before native input", VerifiedEnterForegroundGuard),
    ("verified submission consumes one authorization and emits Enter once", VerifiedSubmitOnce),
    ("submission denies an unverified insertion", VerifiedSubmitRequiresReadback),
    ("submission denies altered target text", VerifiedSubmitRejectsAlteredText),
    ("submission denies focus drift between verification and Enter", VerifiedSubmitFocusDrift),
    ("submission denies an unavailable final target", VerifiedSubmitUnknownTarget),
    ("submission prerequisites avoid target text reads", VerifiedSubmitPrerequisites),
    ("submission cancellation remains reversible before Enter", VerifiedSubmitCancellation),
    ("submit-only authorization emits Enter without a text read", VerifiedSubmitOnly),
    ("a rejected Enter dispatch cannot reuse its authorization", VerifiedSubmitDispatchRejected),
    ("target read-back is bounded and provider failures fail closed", TargetReadbackFailures),
    ("target read-back cancellation is honored", TargetReadbackCancellation),
    ("encrypted history adapter preserves bounded Whisper records", HistoryRetentionAdapter),
    ("provider credentials stay in the protected Windows boundary", ProviderCredentialBoundary),
    ("uninstall cleanup removes only exact-owned Whisper state", UninstallCleanupIsExact),
    ("local model installation verifies and atomically promotes exact bytes", LocalModelInstall),
    ("local model installation rejects oversized responses", LocalModelOversize),
    ("local model installation rejects digest mismatches", LocalModelDigestMismatch),
    ("local model verification detects truncation and change", LocalModelTruncationAndChange),
    ("local model cancellation removes the exact temporary artifact", LocalModelCancellationCleanup),
    ("local model installation rejects a concurrent writer", LocalModelConcurrentInstall),
    ("local model repair replaces an invalid owned artifact", LocalModelRepair),
    ("local model deletion preserves unrelated state", LocalModelOwnedDeletion),
    ("local model installation cleans recognized interrupted downloads", LocalModelInterruptedCleanup),
    ("a verified model lease blocks replacement while native loading begins", LocalModelVerifiedLease),
    ("the packaged CPU runtime loads without opening a model", LocalRuntimePackageProbe),
    ("live-model PCM fixture parsing is bounded and format-strict", LiveModelFixtureParsing),
    ("PCM audio is projected as bounded WAVE without a second audio buffer", PcmWaveProjection),
    ("the local transcriber reuses one runtime and passes language and vocabulary hints", LocalTranscriberSuccess),
    ("local runtime faults are sanitized, content-free, and recoverable", LocalTranscriberFailureRecovery),
    ("local transcription cancellation is honored and classified", LocalTranscriberCancellation),
    ("local transcription rejects short audio, empty text, and oversized output", LocalTranscriberBounds),
    ("explicit local runtime unload releases model resources and reloads lazily", LocalTranscriberUnload)
];

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_LIVE_CAPTURE"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host WASAPI capture returns disposable bounded audio", LiveCapture));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_LIVE_SHORTCUT"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host hooks register, ignore injected input, measure, and unregister", LiveShortcutHooks));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_LIVE_TARGET"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host focused control produces a bounded metadata-only snapshot", LiveTargetInspection));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_LIVE_INSERTION"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host text control receives one clipboard-owned insertion", LiveTextInsertion));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_TARGET_MATRIX"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host WPF target matrix fails closed and preserves editing semantics", LiveWpfTargetMatrix));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_CHROMIUM_TARGET_MATRIX"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host Chromium input and contenteditable verify bounded submission", LiveChromiumTargetMatrix));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_ELECTRON_TARGET_MATRIX"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host Electron input verifies bounded submission", LiveElectronTargetMatrix));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_TERMINAL_TARGET_MATRIX"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host Windows Terminal preserves the separate submit opt-in", LiveWindowsTerminalTargetMatrix));
}

if (string.Equals(
    Environment.GetEnvironmentVariable("SOLTEX_RUN_WHISPER_ELEVATED_TARGET_MATRIX"),
    "1",
    StringComparison.Ordinal))
{
    tests.Add(("owner-host elevated editor fails closed before mutation", LiveElevatedTargetMatrix));
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

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Whisper Windows adapter tests passed.");
return failures == 0 ? 0 : 1;

static Task ShortcutChordMatching()
{
    WhisperShortcutSet set = new(
    [
        WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Ctrl",
            "Alt",
            "Space")
    ]);
    WhisperShortcutMatcher matcher = new(set);

    False(matcher.IsRelevant("Q"));
    Equal(0, matcher.Observe(0x51, "Q", true, TimeSpan.Zero).Count);
    Equal(0, matcher.Observe(0x20, "Space", true, TimeSpan.FromMilliseconds(1)).Count);
    Equal(0, matcher.Observe(0xA4, "Alt", true, TimeSpan.FromMilliseconds(2)).Count);
    IReadOnlyList<WhisperShortcutSignal> pressed = matcher.Observe(
        0xA2,
        "Ctrl",
        true,
        TimeSpan.FromMilliseconds(3));
    Equal(1, pressed.Count);
    Equal(WhisperShortcutTransition.Pressed, pressed[0].Transition);
    Equal(WhisperShortcutAction.PushToTalk, pressed[0].Action);

    Equal(0, matcher.Observe(
        0xA2,
        "Ctrl",
        true,
        TimeSpan.FromMilliseconds(4)).Count);

    IReadOnlyList<WhisperShortcutSignal> released = matcher.Observe(
        0x20,
        "Space",
        false,
        TimeSpan.FromMilliseconds(5));
    Equal(1, released.Count);
    Equal(WhisperShortcutTransition.Released, released[0].Transition);
    return Task.CompletedTask;
}

static Task ShortcutMouseButtons()
{
    WhisperShortcutSet set = new(
    [
        WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Mouse 4")
    ]);
    True(WhisperShortcutRegistrationPolicy.Validate(set).IsValid);
    WhisperShortcutMatcher matcher = new(set);

    IReadOnlyList<WhisperShortcutSignal> pressed = matcher.Observe(
        WindowsShortcutKeyMap.Mouse4InputId,
        "Mouse 4",
        true,
        TimeSpan.FromMilliseconds(1));
    Equal(WhisperShortcutTransition.Pressed, pressed.Single().Transition);
    Equal(0, matcher.Observe(
        WindowsShortcutKeyMap.Mouse4InputId,
        "Mouse 4",
        true,
        TimeSpan.FromMilliseconds(2)).Count);
    IReadOnlyList<WhisperShortcutSignal> released = matcher.Observe(
        WindowsShortcutKeyMap.Mouse4InputId,
        "Mouse 4",
        false,
        TimeSpan.FromMilliseconds(3));
    Equal(WhisperShortcutTransition.Released, released.Single().Transition);
    return Task.CompletedTask;
}

static Task UnsupportedShortcutKey()
{
    WhisperShortcutSet set = new(
    [
        WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Ctrl",
            "Volume Up")
    ]);
    Throws<WhisperShortcutRegistrationException>(() => WindowsShortcutKeyMap.Compile(set));
    return Task.CompletedTask;
}

static async Task AtomicShortcutRollback()
{
    FakeShortcutRegistrationFactory factory = new();
    await using WindowsWhisperShortcutHost host = new(factory);
    List<WhisperShortcutSignal> observed = [];
    WhisperShortcutSet initial = WhisperShortcutSet.CreateDefault();
    await host.RegisterAsync(
        initial,
        (signal, _) =>
        {
            observed.Add(signal);
            return ValueTask.CompletedTask;
        },
        CancellationToken.None);
    FakeShortcutRegistration first = factory.Created.Single();
    True(first.Active);

    factory.FailNext = true;
    await ThrowsAsync<WhisperShortcutRegistrationException>(async () =>
        await host.RegisterAsync(
            new WhisperShortcutSet(
            [
                WhisperShortcutBinding.Create(
                    WhisperShortcutAction.Cancel,
                    "Esc")
            ]),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None));

    True(host.IsRegistered);
    True(first.Active);
    await first.EmitAsync(new WhisperShortcutSignal(
        WhisperShortcutAction.Cancel,
        WhisperShortcutTransition.Pressed,
        TimeSpan.FromSeconds(1)));
    Equal(1, observed.Count);
}

static async Task TargetCategories()
{
    (WindowsWhisperTargetObservation Observation, WhisperTargetKind Expected)[] cases =
    [
        (TargetObservation("notepad", WindowsWhisperControlKind.Edit, valuePattern: true),
            WhisperTargetKind.PlainText),
        (TargetObservation("wordpad", WindowsWhisperControlKind.Document, textPattern: true),
            WhisperTargetKind.RichText),
        (TargetObservation("pwsh", WindowsWhisperControlKind.Edit, valuePattern: true),
            WhisperTargetKind.Terminal),
        (TargetObservation("chrome", WindowsWhisperControlKind.Document, textPattern: true),
            WhisperTargetKind.Browser),
        (TargetObservation(
                "soltex-electron-harness",
                WindowsWhisperControlKind.Document,
                textPattern: true,
                frameworkId: "Chrome"),
            WhisperTargetKind.Browser),
        (TargetObservation("code", WindowsWhisperControlKind.Document, textPattern: true),
            WhisperTargetKind.Editor),
        (TargetObservation(
                "code",
                WindowsWhisperControlKind.Document,
                textPattern: true,
                frameworkId: "Chrome"),
            WhisperTargetKind.Editor)
    ];

    foreach ((WindowsWhisperTargetObservation observation, WhisperTargetKind expected) in cases)
    {
        WindowsWhisperTargetInspector inspector = new(
            new ScriptedTargetInspectionBackend(() => observation),
            TimeSpan.FromMilliseconds(250));
        WhisperTargetSnapshot? snapshot = await inspector.InspectAsync(CancellationToken.None);
        True(snapshot is not null);
        Equal(expected, snapshot!.Context.Kind);
        Equal(observation.ProcessId, snapshot.Identity.ProcessId);
        Equal(WhisperTargetInspectionFailureKind.None, inspector.LastFailure);
    }
}

static async Task TargetCapabilities()
{
    WindowsWhisperTargetObservation observation = TargetObservation(
        "writer",
        WindowsWhisperControlKind.Document,
        textPattern: true,
        textPattern2: true,
        selection: true);
    WindowsWhisperTargetInspector inspector = new(
        new ScriptedTargetInspectionBackend(() => observation),
        TimeSpan.FromMilliseconds(250));

    WhisperTargetSnapshot snapshot = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Expected a known rich-text target.");
    True(snapshot.Context.IsEditable);
    False(snapshot.Context.IsReadOnly);
    True(snapshot.Context.Capabilities.SupportsTextPattern);
    True(snapshot.Context.Capabilities.SupportsTextPattern2);
    True(snapshot.Context.Capabilities.SupportsSelection);
    True(snapshot.Context.Capabilities.SupportsCaret);
    Equal(WhisperTargetIntegrityLevel.Medium, snapshot.Context.IntegrityLevel);
}

static async Task TargetSafetyStates()
{
    WindowsWhisperTargetInspector passwordInspector = new(
        new ScriptedTargetInspectionBackend(() => TargetObservation(
            "browser",
            WindowsWhisperControlKind.Edit,
            valuePattern: true,
            password: true)),
        TimeSpan.FromMilliseconds(250));
    WhisperTargetSnapshot password = await passwordInspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Expected a protected target snapshot.");
    True(password.Context.IsPassword);
    False(password.Context.IsEditable);

    WindowsWhisperTargetInspector readOnlyInspector = new(
        new ScriptedTargetInspectionBackend(() => TargetObservation(
            "reader",
            WindowsWhisperControlKind.Edit,
            valuePattern: true,
            valueReadOnly: true)),
        TimeSpan.FromMilliseconds(250));
    WhisperTargetSnapshot readOnly = await readOnlyInspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Expected a read-only target snapshot.");
    True(readOnly.Context.IsReadOnly);
    False(readOnly.Context.IsEditable);

    WindowsWhisperTargetInspector unknownInspector = new(
        new ScriptedTargetInspectionBackend(() => TargetObservation(
            "surface",
            WindowsWhisperControlKind.Unknown)),
        TimeSpan.FromMilliseconds(250));
    Equal<WhisperTargetSnapshot?>(
        null,
        await unknownInspector.InspectAsync(CancellationToken.None));
    Equal(WhisperTargetInspectionFailureKind.UnknownTarget, unknownInspector.LastFailure);
}

static async Task TargetTimeout()
{
    WindowsWhisperTargetInspector inspector = new(
        new ScriptedTargetInspectionBackend(() =>
        {
            Thread.Sleep(80);
            return TargetObservation(
                "notepad",
                WindowsWhisperControlKind.Edit,
                valuePattern: true);
        }),
        TimeSpan.FromMilliseconds(10));
    Stopwatch timer = Stopwatch.StartNew();
    WhisperTargetSnapshot? snapshot = await inspector.InspectAsync(CancellationToken.None);
    timer.Stop();

    Equal<WhisperTargetSnapshot?>(null, snapshot);
    Equal(WhisperTargetInspectionFailureKind.TimedOut, inspector.LastFailure);
    True(timer.Elapsed < TimeSpan.FromMilliseconds(250));
    await Task.Delay(100);
}

static async Task TargetProviderFailure()
{
    WindowsWhisperTargetInspector inspector = new(
        new ScriptedTargetInspectionBackend(() =>
            throw new InvalidOperationException("Scripted inaccessible provider.")),
        TimeSpan.FromMilliseconds(250));
    Equal<WhisperTargetSnapshot?>(
        null,
        await inspector.InspectAsync(CancellationToken.None));
    Equal(WhisperTargetInspectionFailureKind.ProviderUnavailable, inspector.LastFailure);
}

static async Task TargetCancellation()
{
    WindowsWhisperTargetInspector inspector = new(
        new ScriptedTargetInspectionBackend(() =>
        {
            Thread.Sleep(80);
            return TargetObservation(
                "notepad",
                WindowsWhisperControlKind.Edit,
                valuePattern: true);
        }),
        TimeSpan.FromMilliseconds(250));
    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(10));
    await ThrowsAsync<OperationCanceledException>(async () =>
        await inspector.InspectAsync(cancellation.Token));
    await Task.Delay(100);
}

static WindowsWhisperTargetObservation TargetObservation(
    string processName,
    WindowsWhisperControlKind controlKind,
    bool valuePattern = false,
    bool valueReadOnly = false,
    bool textPattern = false,
    bool textPattern2 = false,
    bool selection = false,
    bool password = false,
    string frameworkId = "Win32") => new(
        ProcessId: 2048,
        processName,
        frameworkId,
        RuntimeId: [42, 7, 11],
        WhisperTargetIntegrityLevel.Medium,
        controlKind,
        IsEnabled: true,
        IsKeyboardFocusable: true,
        password,
        valuePattern,
        valueReadOnly,
        textPattern,
        textPattern2,
        TextReadOnlyKnown: textPattern,
        TextIsReadOnly: false,
        selection);

static async Task DirectInsertionPrecedesClipboard()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured);
    FakeInsertionPlatform platform = new() { DirectResult = true };
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    Equal(WhisperInsertionMethod.AutomationValue, result.Method);
    True(result.MutationDispatched);
    Equal(1, platform.DirectCount);
    Equal(0, platform.StageCount);
    Equal(0, platform.PasteCount);
}

static async Task ClipboardOwnershipRestore()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeInsertionPlatform platform = new()
    {
        DirectResult = false,
        RestoreOutcome = WhisperClipboardRestoreOutcome.Restored
    };
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    Equal(WhisperInsertionMethod.ClipboardPaste, result.Method);
    True(result.MutationDispatched);
    Equal(WhisperClipboardRestoreOutcome.Restored, result.ClipboardRestore);
    Equal(1, platform.StageCount);
    Equal(1, platform.PasteCount);
    Equal(1, platform.LastLease?.RestoreCount);
    True(WindowsWhisperTextDelivery.ClipboardSettleDelay >= TimeSpan.FromMilliseconds(200));
    True(WindowsWhisperTextDelivery.ClipboardSettleDelay <= TimeSpan.FromMilliseconds(500));
}

static async Task ClipboardOwnershipLost()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeInsertionPlatform platform = new()
    {
        RestoreOutcome = WhisperClipboardRestoreOutcome.SkippedOwnershipChanged
    };
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    True(result.MutationDispatched);
    Equal(
        WhisperClipboardRestoreOutcome.SkippedOwnershipChanged,
        result.ClipboardRestore);
}

static async Task DeliveryFocusDrift()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperTargetSnapshot changed = DeliveryTarget("mail", "el-2", processId: 42);
    WhisperScriptedTargetInspector inspector = new(captured, changed);
    FakeInsertionPlatform platform = new();
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    Equal(WhisperInsertionMethod.ClipboardCopy, result.Method);
    Equal(WhisperInsertionFallbackReason.TargetChanged, result.FallbackReason);
    False(result.MutationDispatched);
    True(result.Copied);
    Equal(0, platform.PasteCount);
    Equal(0, platform.LastLease?.RestoreCount);
}

static async Task DeliveryUnknownTarget()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new((WhisperTargetSnapshot?)null);
    FakeInsertionPlatform platform = new();
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    Equal(WhisperInsertionMethod.ClipboardCopy, result.Method);
    Equal(WhisperInsertionFallbackReason.TargetUnknown, result.FallbackReason);
    Equal(1, platform.CopyCount);
    Equal(0, platform.PasteCount);
}

static async Task DeliveryPasteRejected()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeInsertionPlatform platform = new() { PasteResult = false };
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        DeliveryRequest(captured),
        CancellationToken.None);
    Equal(WhisperInsertionMethod.ClipboardCopy, result.Method);
    Equal(WhisperInsertionFallbackReason.PasteRejected, result.FallbackReason);
    False(result.MutationDispatched);
    True(result.Copied);
}

static async Task DeliveryCancellationRestores()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    using CancellationTokenSource cancellation = new();
    CancelingTargetInspector inspector = new(captured, cancellation);
    FakeInsertionPlatform platform = new()
    {
        RestoreOutcome = WhisperClipboardRestoreOutcome.Restored
    };
    WindowsWhisperTextDelivery delivery = new(inspector, platform);

    await ThrowsAsync<OperationCanceledException>(async () =>
        await delivery.DeliverAsync(DeliveryRequest(captured), cancellation.Token));
    Equal(0, platform.PasteCount);
    Equal(1, platform.LastLease?.RestoreCount);
}

static async Task VerifiedSubmitOnce()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);
    True(result.EnterDispatched);
    Equal(WhisperSubmitDispatchOutcome.Dispatched, result.Outcome);
    True(result.Verification.Verified);
    Equal(1, reader.ReadCount);
    Equal(1, platform.DispatchCount);
    True(platform.FirstConsume);
    False(platform.SecondConsume);
}

static Task VerifiedEnterInputEncoding()
{
    Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<PasteNative.Input>());
    Equal((nint)(IntPtr.Size == 8 ? 8 : 4),
        Marshal.OffsetOf<PasteNative.Input>(nameof(PasteNative.Input.Data)));

    PasteNative.Input[] inputs =
        WindowsWhisperSubmitPlatform.CreateEnterInputs();

    Equal(2, inputs.Length);
    Equal((ushort)0x0D, inputs[0].Data.Keyboard.VirtualKey);
    Equal((ushort)0, inputs[0].Data.Keyboard.ScanCode);
    Equal(0u, inputs[0].Data.Keyboard.Flags);
    Equal((nuint)0, inputs[0].Data.Keyboard.ExtraInfo);
    Equal((ushort)0x0D, inputs[1].Data.Keyboard.VirtualKey);
    Equal((ushort)0, inputs[1].Data.Keyboard.ScanCode);
    Equal(0x0002u, inputs[1].Data.Keyboard.Flags);
    Equal((nuint)0, inputs[1].Data.Keyboard.ExtraInfo);
    return Task.CompletedTask;
}

static async Task VerifiedEnterForegroundGuard()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    int sendCount = 0;
    WindowsWhisperSubmitPlatform platform = new(
        foregroundBelongsToProcess: _ => false,
        modifierIsDown: () => false,
        sendInput: inputs =>
        {
            sendCount++;
            return checked((uint)inputs.Length);
        });
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);

    True(result.Authorization.Allowed);
    False(result.EnterDispatched);
    Equal(WhisperSubmitDispatchOutcome.DispatchRejected, result.Outcome);
    Equal(0, sendCount);
    False(result.Authorization.TryConsume());
}

static async Task VerifiedSubmitRequiresReadback()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);
    WhisperVerifiedSubmitRequest request = VerifiedSubmitRequest(
        captured,
        delivery: NoMutationDelivery());

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        request,
        CancellationToken.None);
    False(result.EnterDispatched);
    False(result.Authorization.Allowed);
    Equal(WhisperSubmitDispatchOutcome.Denied, result.Outcome);
    Equal(0, reader.ReadCount);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitRejectsAlteredText()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeTargetTextReader reader = new("hello from Soltex altered");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);
    False(result.EnterDispatched);
    False(result.Verification.Verified);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitFocusDrift()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperTargetSnapshot changed = DeliveryTarget("mail", "el-2", processId: 42);
    WhisperScriptedTargetInspector inspector = new(captured, changed);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);
    False(result.EnterDispatched);
    Equal(WhisperTargetDrift.ProcessChanged, result.Authorization.Drift);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitUnknownTarget()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(
        captured,
        (WhisperTargetSnapshot?)null);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);
    False(result.EnterDispatched);
    Equal(WhisperTargetDrift.TargetUnknown, result.Authorization.Drift);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitPrerequisites()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitRequest warningNotAccepted = VerifiedSubmitRequest(
        captured,
        firstUseWarningAccepted: false);
    WhisperVerifiedSubmitResult warningResult = await submitter.SubmitAsync(
        warningNotAccepted,
        CancellationToken.None);
    False(warningResult.Authorization.Allowed);
    Equal(0, reader.ReadCount);
    Equal(0, platform.DispatchCount);

    WhisperDeliveryDecision missingOrigin = new(
        WhisperDeliveryKind.InsertAndSubmit,
        "hello from Soltex",
        WhisperSubmitOrigin.None,
        RestoreClipboard: false,
        "invalid origin fixture");
    WhisperVerifiedSubmitResult originResult = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured, decision: missingOrigin),
        CancellationToken.None);
    False(originResult.Authorization.Allowed);
    Equal(0, reader.ReadCount);
    Equal(0, platform.DispatchCount);

    WhisperDeliveryDecision insertOnly = new(
        WhisperDeliveryKind.InsertText,
        "hello from Soltex",
        WhisperSubmitOrigin.None,
        RestoreClipboard: false,
        "insert only");
    WhisperVerifiedSubmitResult insertResult = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured, decision: insertOnly),
        CancellationToken.None);
    Equal(WhisperSubmitDispatchOutcome.NotRequested, insertResult.Outcome);
    Equal(0, reader.ReadCount);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitCancellation()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        cancellation.Token);
    False(result.Authorization.Allowed);
    False(result.EnterDispatched);
    Equal(0, reader.ReadCount);
    Equal(0, platform.DispatchCount);
}

static async Task VerifiedSubmitOnly()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured);
    FakeTargetTextReader reader = new("ignored");
    FakeSubmitPlatform platform = new(dispatchResult: true);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);
    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.SubmitOnly,
        string.Empty,
        WhisperSubmitOrigin.DedicatedShortcut,
        RestoreClipboard: false,
        "submit the focused target");

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(
            captured,
            decision: decision,
            delivery: NoMutationDelivery()),
        CancellationToken.None);
    True(result.EnterDispatched);
    Equal(WhisperVerificationMethod.None, result.Verification.Method);
    Equal(0, reader.ReadCount);
    Equal(1, platform.DispatchCount);
}

static async Task VerifiedSubmitDispatchRejected()
{
    WhisperTargetSnapshot captured = DeliveryTarget("chat", "el-1");
    WhisperScriptedTargetInspector inspector = new(captured, captured);
    FakeTargetTextReader reader = new("hello from Soltex");
    FakeSubmitPlatform platform = new(dispatchResult: false);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector, reader, platform);

    WhisperVerifiedSubmitResult result = await submitter.SubmitAsync(
        VerifiedSubmitRequest(captured),
        CancellationToken.None);
    False(result.EnterDispatched);
    Equal(WhisperSubmitDispatchOutcome.DispatchRejected, result.Outcome);
    True(platform.FirstConsume);
    False(platform.SecondConsume);
}

static async Task TargetReadbackFailures()
{
    WhisperTargetSnapshot target = DeliveryTarget("chat", "el-1");
    WindowsWhisperTargetTextReader success = new(
        new ScriptedTargetTextBackend(() => new WindowsWhisperTargetReadObservation(
            "hello from Soltex",
            WhisperVerificationMethod.AutomationTextRead)),
        TimeSpan.FromMilliseconds(100));
    WhisperTargetReadback? readback = await success.ReadAsync(
        target,
        CancellationToken.None);
    Equal("hello from Soltex", readback?.Text);

    WindowsWhisperTargetTextReader oversized = new(
        new ScriptedTargetTextBackend(() => new WindowsWhisperTargetReadObservation(
            new string('x', WhisperLimits.MaximumReadbackCharacters + 1),
            WhisperVerificationMethod.AutomationTextRead)),
        TimeSpan.FromMilliseconds(100));
    Equal<WhisperTargetReadback?>(
        null,
        await oversized.ReadAsync(target, CancellationToken.None));

    WindowsWhisperTargetTextReader throwing = new(
        new ScriptedTargetTextBackend(() =>
            throw new InvalidOperationException("provider fixture")),
        TimeSpan.FromMilliseconds(100));
    Equal<WhisperTargetReadback?>(
        null,
        await throwing.ReadAsync(target, CancellationToken.None));

    WindowsWhisperTargetTextReader slow = new(
        new ScriptedTargetTextBackend(() =>
        {
            Thread.Sleep(100);
            return new WindowsWhisperTargetReadObservation(
                "late",
                WhisperVerificationMethod.AutomationTextRead);
        }),
        TimeSpan.FromMilliseconds(20));
    Stopwatch timer = Stopwatch.StartNew();
    Equal<WhisperTargetReadback?>(
        null,
        await slow.ReadAsync(target, CancellationToken.None));
    True(timer.Elapsed < TimeSpan.FromMilliseconds(90));
}

static async Task TargetReadbackCancellation()
{
    WhisperTargetSnapshot target = DeliveryTarget("chat", "el-1");
    using ManualResetEventSlim started = new();
    WindowsWhisperTargetTextReader reader = new(
        new ScriptedTargetTextBackend(() =>
        {
            started.Set();
            Thread.Sleep(100);
            return new WindowsWhisperTargetReadObservation(
                "late",
                WhisperVerificationMethod.AutomationTextRead);
        }),
        TimeSpan.FromMilliseconds(200));
    using CancellationTokenSource cancellation = new();
    Task<WhisperTargetReadback?> read = reader
        .ReadAsync(target, cancellation.Token)
        .AsTask();
    True(started.Wait(TimeSpan.FromSeconds(1)));
    cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(async () => await read);
}

static WhisperTargetSnapshot DeliveryTarget(
    string processName,
    string runtimeId,
    int processId = 7) => new(
        new WhisperTargetIdentity(processId, processName, runtimeId),
        new WhisperTargetContext(
            processName,
            WhisperTargetKind.PlainText,
            isKnown: true,
            isEditable: true,
            isPassword: false,
            isReadOnly: false,
            isElevated: false,
            new WhisperTargetCapabilities(
                SupportsValuePattern: true,
                SupportsTextPattern: true,
                SupportsTextPattern2: false,
                SupportsSelection: true,
                SupportsCaret: true)));

static WhisperTextDeliveryRequest DeliveryRequest(WhisperTargetSnapshot captured) => new(
    new WhisperDeliveryDecision(
        WhisperDeliveryKind.InsertText,
        "hello from Soltex",
        WhisperSubmitOrigin.None,
        RestoreClipboard: true,
        "insert"),
    captured);

static WhisperVerifiedSubmitRequest VerifiedSubmitRequest(
    WhisperTargetSnapshot captured,
    WhisperDeliveryDecision? decision = null,
    WhisperTextDeliveryResult? delivery = null,
    bool firstUseWarningAccepted = true) => new(
        decision ?? new WhisperDeliveryDecision(
            WhisperDeliveryKind.InsertAndSubmit,
            "hello from Soltex",
            WhisperSubmitOrigin.TerminalPhrase,
            RestoreClipboard: false,
            "authorized fixture"),
        captured,
        delivery ?? new WhisperTextDeliveryResult(
            WhisperInsertionMethod.ClipboardPaste,
            WhisperInsertionFallbackReason.DirectInsertionUnavailable,
            WhisperClipboardRestoreOutcome.NotRequested,
            MutationDispatched: true,
            Copied: false),
        firstUseWarningAccepted);

static WhisperTextDeliveryResult NoMutationDelivery() => new(
    WhisperInsertionMethod.None,
    WhisperInsertionFallbackReason.PasteRejected,
    WhisperClipboardRestoreOutcome.NotRequested,
    MutationDispatched: false,
    Copied: false);

static async Task LiveShortcutHooks()
{
    await using WindowsWhisperShortcutHost host = new();
    int observedSignals = 0;
    WhisperShortcutSet set = new(
    [
        WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Ctrl",
            "Shift",
            "F24")
    ]);
    await host.RegisterAsync(
        set,
        (_, _) =>
        {
            Interlocked.Increment(ref observedSignals);
            return ValueTask.CompletedTask;
        },
        CancellationToken.None);
    True(host.IsRegistered);

    SendInjectedShortcut();
    await Task.Delay(150);
    WhisperShortcutPerformanceSnapshot performance = host.Performance;
    True(performance.SampleCount >= 6);
    Equal(0, Volatile.Read(ref observedSignals));
    Console.WriteLine(
        $"MEASURE whisper_hook callbacks={performance.SampleCount} " +
        $"local_p50_us={performance.P50Microseconds:F2} " +
        $"local_p95_us={performance.P95Microseconds:F2} " +
        $"local_p99_us={performance.P99Microseconds:F2} injected_signals=0");
}

static async Task LiveTargetInspection()
{
    WindowsWhisperTargetInspector inspector = new();
    Stopwatch timer = Stopwatch.StartNew();
    WhisperTargetSnapshot snapshot = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException(
            $"Focused-control inspection was unavailable ({inspector.LastFailure}).");
    timer.Stop();

    True(snapshot.Identity.ElementRuntimeId.Length <=
         WhisperTargetIdentity.MaximumRuntimeIdCharacters);
    True(snapshot.Context.IsKnown);
    Console.WriteLine(
        $"MEASURE whisper_target category={snapshot.Context.Kind} " +
        $"integrity={snapshot.Context.IntegrityLevel} " +
        $"value_pattern={snapshot.Context.Capabilities.SupportsValuePattern} " +
        $"text_pattern={snapshot.Context.Capabilities.SupportsTextPattern} " +
        $"text_pattern2={snapshot.Context.Capabilities.SupportsTextPattern2} " +
        $"duration_ms={timer.Elapsed.TotalMilliseconds:F2} content_read=0");
}

static async Task LiveTextInsertion()
{
    const string prefix = "existing ";
    const string inserted = "soltex insertion probe";
    await using LiveTextTarget target = await LiveTextTarget.CreateAsync(prefix);
    await target.FocusAsync();

    WindowsWhisperTargetInspector inspector = new();
    WhisperTargetSnapshot captured = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException(
            $"Controlled target inspection was unavailable ({inspector.LastFailure}).");
    WindowsWhisperTextDelivery delivery = new(inspector);
    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.InsertText,
        inserted,
        WhisperSubmitOrigin.None,
        RestoreClipboard: true,
        "owner-controlled insertion proof");

    Stopwatch timer = Stopwatch.StartNew();
    WhisperTextDeliveryResult result = await delivery.DeliverAsync(
        new WhisperTextDeliveryRequest(decision, captured),
        CancellationToken.None);
    await target.WaitForTextAsync(prefix.Length + inserted.Length);
    timer.Stop();

    Equal(prefix + inserted, await target.GetTextAsync());
    Equal(WhisperInsertionMethod.ClipboardPaste, result.Method);
    True(result.MutationDispatched);
    Equal(WhisperClipboardRestoreOutcome.Restored, result.ClipboardRestore);

    await target.SelectAllAsync();
    WhisperTargetSnapshot replacementTarget = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("The controlled replacement target was unavailable.");
    Stopwatch directTimer = Stopwatch.StartNew();
    WhisperTextDeliveryResult directResult = await delivery.DeliverAsync(
        DeliveryRequest(replacementTarget),
        CancellationToken.None);
    await target.WaitForTextAsync("hello from Soltex".Length);
    directTimer.Stop();
    Equal("hello from Soltex", await target.GetTextAsync());
    Equal(WhisperInsertionMethod.AutomationValue, directResult.Method);

    WhisperDeliveryDecision submitDecision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        "hello from Soltex",
        WhisperSubmitOrigin.TerminalPhrase,
        RestoreClipboard: false,
        "owner-controlled verified-submit proof");
    WindowsWhisperVerifiedSubmitter submitter = new(inspector);
    Stopwatch submitTimer = Stopwatch.StartNew();
    WhisperVerifiedSubmitResult submitResult = await submitter.SubmitAsync(
        new WhisperVerifiedSubmitRequest(
            submitDecision,
            replacementTarget,
            directResult,
            FirstUseWarningAccepted: true),
        CancellationToken.None);
    await target.WaitForEnterAsync(expectedCount: 1);
    submitTimer.Stop();
    True(submitResult.Authorization.Allowed);
    True(submitResult.Verification.Verified);
    True(submitResult.EnterDispatched);
    Equal(1, target.EnterCount);
    Console.WriteLine(
        $"MEASURE whisper_insertion method={result.Method} " +
        $"restore={result.ClipboardRestore} duration_ms={timer.Elapsed.TotalMilliseconds:F2} " +
        $"direct_method={directResult.Method} direct_ms={directTimer.Elapsed.TotalMilliseconds:F2} " +
        $"verified_submit_ms={submitTimer.Elapsed.TotalMilliseconds:F2} " +
        "input_events=6 enter_events=1 content_logged=0");
}

static async Task LiveWpfTargetMatrix()
{
    await using LiveWpfTargetMatrixHost target = await LiveWpfTargetMatrixHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();
    WindowsWhisperTextDelivery delivery = new(inspector);

    await target.FocusAsync(WpfMatrixTarget.TextBox);
    await target.SelectAllTextBoxAsync();
    WhisperTargetSnapshot plain = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled WPF TextBox inspection was unavailable.");
    Equal(WhisperTargetKind.PlainText, plain.Context.Kind);
    True(plain.Context.IsEditable);
    WhisperTextDeliveryResult plainResult = await delivery.DeliverAsync(
        MatrixDeliveryRequest(plain),
        CancellationToken.None);
    await target.WaitForTextAsync(WpfMatrixTarget.TextBox, "matrix replacement".Length);
    Equal(WhisperInsertionMethod.AutomationValue, plainResult.Method);

    await target.FocusAsync(WpfMatrixTarget.RichTextBox);
    WhisperTargetSnapshot rich = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled WPF RichTextBox inspection was unavailable.");
    Equal(WhisperTargetKind.RichText, rich.Context.Kind);
    WhisperTextDeliveryResult richResult = await delivery.DeliverAsync(
        MatrixDeliveryRequest(rich),
        CancellationToken.None);
    await target.WaitForTextAsync(WpfMatrixTarget.RichTextBox, "matrix replacement".Length);
    True(richResult.MutationDispatched);

    await target.FocusAsync(WpfMatrixTarget.PasswordBox);
    WhisperTargetSnapshot password = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled WPF PasswordBox inspection was unavailable.");
    True(password.Context.IsPassword);
    WhisperInsertionAuthorization passwordAuthorization = WhisperInsertionPolicy.Evaluate(
        MatrixDeliveryRequest(password),
        password);
    Equal(WhisperInsertionAction.Copy, passwordAuthorization.Action);
    Equal(
        WhisperInsertionFallbackReason.ProtectedField,
        passwordAuthorization.FallbackReason);

    await target.FocusAsync(WpfMatrixTarget.ReadOnlyTextBox);
    WhisperTargetSnapshot readOnly = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled read-only WPF target inspection was unavailable.");
    True(readOnly.Context.IsReadOnly);
    WhisperInsertionAuthorization readOnlyAuthorization = WhisperInsertionPolicy.Evaluate(
        MatrixDeliveryRequest(readOnly),
        readOnly);
    Equal(WhisperInsertionAction.Copy, readOnlyAuthorization.Action);
    Equal(
        WhisperInsertionFallbackReason.TargetNotEditable,
        readOnlyAuthorization.FallbackReason);

    await target.FocusAsync(WpfMatrixTarget.TextBox);
    WhisperTargetSnapshot captured = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled focus-drift source was unavailable.");
    await target.FocusAsync(WpfMatrixTarget.SecondTextBox);
    WhisperTargetSnapshot changed = await inspector.InspectAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Controlled focus-drift destination was unavailable.");
    WhisperInsertionAuthorization driftAuthorization = WhisperInsertionPolicy.Evaluate(
        MatrixDeliveryRequest(captured),
        changed);
    Equal(WhisperInsertionAction.Copy, driftAuthorization.Action);
    Equal(
        WhisperInsertionFallbackReason.TargetChanged,
        driftAuthorization.FallbackReason);

    Console.WriteLine(
        $"MEASURE whisper_wpf_matrix plain={plain.Context.Kind}:{plainResult.Method} " +
        $"rich={rich.Context.Kind}:{richResult.Method} " +
        $"password=protected:{passwordAuthorization.FallbackReason} " +
        $"readonly={readOnlyAuthorization.FallbackReason} " +
        $"drift={driftAuthorization.FallbackReason} content_logged=0");
}

static async Task LiveChromiumTargetMatrix()
{
    await using LiveChromiumTargetHost target = await LiveChromiumTargetHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();
    WindowsWhisperTextDelivery delivery = new(inspector);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector);

    await target.FocusInputAsync();
    WhisperTargetSnapshot captured = await WaitForControlledBrowserTargetAsync(inspector);
    Equal(WhisperTargetKind.Browser, captured.Context.Kind);
    True(captured.Context.IsEditable);

    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        LiveChromiumTargetHost.InputProbeText,
        WhisperSubmitOrigin.DedicatedShortcut,
        RestoreClipboard: true,
        "owner-controlled Chromium matrix");
    Stopwatch insertionTimer = Stopwatch.StartNew();
    WhisperTextDeliveryResult insertion = await delivery.DeliverAsync(
        new WhisperTextDeliveryRequest(decision, captured),
        CancellationToken.None);
    insertionTimer.Stop();
    True(insertion.MutationDispatched);
    False(insertion.Copied);

    Stopwatch submitTimer = Stopwatch.StartNew();
    WhisperVerifiedSubmitResult submission = await submitter.SubmitAsync(
        new WhisperVerifiedSubmitRequest(
            decision,
            captured,
            insertion,
            FirstUseWarningAccepted: true),
        CancellationToken.None);
    await target.WaitForEnterCountAsync(expectedCount: 1);
    submitTimer.Stop();

    True(submission.Authorization.Allowed);
    True(submission.Verification.Verified);
    True(submission.EnterDispatched);

    WhisperTargetSnapshot contentEditable = await WaitForControlledBrowserTargetAsync(inspector);
    False(contentEditable.Identity.Equals(captured.Identity));
    WhisperDeliveryDecision contentEditableDecision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        LiveChromiumTargetHost.ContentEditableProbeText,
        WhisperSubmitOrigin.DedicatedShortcut,
        RestoreClipboard: true,
        "owner-controlled Chromium contenteditable matrix");
    WhisperTextDeliveryResult contentEditableInsertion = await delivery.DeliverAsync(
        new WhisperTextDeliveryRequest(contentEditableDecision, contentEditable),
        CancellationToken.None);
    True(contentEditableInsertion.MutationDispatched);
    WhisperVerifiedSubmitResult contentEditableSubmission = await submitter.SubmitAsync(
        new WhisperVerifiedSubmitRequest(
            contentEditableDecision,
            contentEditable,
            contentEditableInsertion,
            FirstUseWarningAccepted: true),
        CancellationToken.None);
    await target.WaitForEnterCountAsync(expectedCount: 2);
    True(contentEditableSubmission.Authorization.Allowed);
    True(contentEditableSubmission.Verification.Verified);
    True(contentEditableSubmission.EnterDispatched);
    Console.WriteLine(
        $"MEASURE whisper_chromium_matrix category={captured.Context.Kind} " +
        $"input_method={insertion.Method} contenteditable_method={contentEditableInsertion.Method} " +
        $"insert_ms={insertionTimer.Elapsed.TotalMilliseconds:F2} " +
        $"verified_submit_ms={submitTimer.Elapsed.TotalMilliseconds:F2} " +
        "enter_events=2 content_logged=0");
}

static async Task LiveElectronTargetMatrix()
{
    await using LiveElectronTargetHost target = await LiveElectronTargetHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();
    WindowsWhisperTextDelivery delivery = new(inspector);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector);

    await target.FocusInputAsync();
    WhisperTargetSnapshot captured = await WaitForControlledBrowserTargetAsync(inspector);
    Equal(WhisperTargetKind.Browser, captured.Context.Kind);
    True(captured.Context.IsEditable);
    Equal("electron", captured.Context.ProcessName);

    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        LiveElectronTargetHost.ProbeText,
        WhisperSubmitOrigin.DedicatedShortcut,
        RestoreClipboard: true,
        "owner-controlled Electron matrix");
    Stopwatch insertionTimer = Stopwatch.StartNew();
    WhisperTextDeliveryResult insertion = await delivery.DeliverAsync(
        new WhisperTextDeliveryRequest(decision, captured),
        CancellationToken.None);
    insertionTimer.Stop();
    True(insertion.MutationDispatched);
    False(insertion.Copied);

    Stopwatch submitTimer = Stopwatch.StartNew();
    WhisperVerifiedSubmitResult submission = await submitter.SubmitAsync(
        new WhisperVerifiedSubmitRequest(
            decision,
            captured,
            insertion,
            FirstUseWarningAccepted: true),
        CancellationToken.None);
    True(submission.Authorization.Allowed);
    True(submission.Verification.Verified);
    True(submission.EnterDispatched);
    await target.WaitForEnterAsync();
    submitTimer.Stop();
    Console.WriteLine(
        $"MEASURE whisper_electron_matrix category={captured.Context.Kind} " +
        $"method={insertion.Method} insert_ms={insertionTimer.Elapsed.TotalMilliseconds:F2} " +
        $"verified_submit_ms={submitTimer.Elapsed.TotalMilliseconds:F2} " +
        "enter_events=1 content_logged=0");
}

static async Task LiveWindowsTerminalTargetMatrix()
{
    await using LiveWindowsTerminalTargetHost target =
        await LiveWindowsTerminalTargetHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();

    await target.FocusTerminalAsync();
    WhisperTargetSnapshot captured = await WaitForControlledTerminalTargetAsync(inspector);
    Equal(WhisperTargetKind.Terminal, captured.Context.Kind);
    True(captured.Context.IsEditable);
    False(captured.Context.IsElevated);

    WhisperPipelineResult pipeline = new(
        "echo soltex terminal matrix",
        submitRequested: true,
        WhisperSubmitOrigin.TerminalPhrase,
        Array.Empty<string>());
    WhisperDeliveryPolicy policy = new();
    WhisperAppProfile disabledProfile = new(
        captured.Context.ProcessName,
        autoSendAllowed: true,
        terminalAutoSendAllowed: false);
    WhisperDeliveryDecision disabled = policy.Evaluate(
        pipeline,
        captured.Context,
        disabledProfile,
        autoSendEnabled: true);
    Equal(WhisperDeliveryKind.InsertText, disabled.Kind);
    Equal(WhisperSubmitOrigin.None, disabled.SubmitOrigin);

    WhisperAppProfile enabledProfile = new(
        captured.Context.ProcessName,
        autoSendAllowed: true,
        terminalAutoSendAllowed: true);
    WhisperDeliveryDecision enabled = policy.Evaluate(
        pipeline,
        captured.Context,
        enabledProfile,
        autoSendEnabled: true);
    Equal(WhisperDeliveryKind.InsertAndSubmit, enabled.Kind);
    Equal(WhisperSubmitOrigin.TerminalPhrase, enabled.SubmitOrigin);
    Console.WriteLine(
        $"MEASURE whisper_terminal_matrix category={captured.Context.Kind} " +
        $"integrity={captured.Context.IntegrityLevel} disabled={disabled.Kind} " +
        $"opted_in={enabled.Kind} submit_dispatches=0 content_logged=0");
}

static async Task LiveElevatedTargetMatrix()
{
    await using LiveElevatedTargetHost target = await LiveElevatedTargetHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();

    await target.FocusEditorAsync();
    WhisperTargetIntegrityLevel processIntegrity =
        WindowsProcessIntegrity.Read(target.ProcessId);
    True(processIntegrity is
        WhisperTargetIntegrityLevel.High or
        WhisperTargetIntegrityLevel.System or
        WhisperTargetIntegrityLevel.Protected);
    WhisperTargetSnapshot? captured = await inspector.InspectAsync(CancellationToken.None);
    True(captured is null || captured.Context.IsElevated);
    WhisperTargetContext context = captured?.Context ?? WhisperTargetContext.Unknown;

    WhisperPipelineResult pipeline = new(
        "elevated matrix probe",
        submitRequested: false,
        WhisperSubmitOrigin.None,
        Array.Empty<string>());
    WhisperDeliveryDecision decision = new WhisperDeliveryPolicy().Evaluate(
        pipeline,
        context,
        profile: null,
        autoSendEnabled: false,
        soltexIsElevated: false);
    Equal(WhisperDeliveryKind.CopyText, decision.Kind);
    Equal(WhisperSubmitOrigin.None, decision.SubmitOrigin);
    Console.WriteLine(
        $"MEASURE whisper_elevated_matrix category={context.Kind} " +
        $"integrity={processIntegrity} editable={context.IsEditable} " +
        $"inspection={inspector.LastFailure} " +
        $"decision={decision.Kind} " +
        "mutation_dispatches=0 submit_dispatches=0 content_logged=0");
}

static async Task LiveWinUiTargetMatrix()
{
    await using LiveWinUiTargetHost target = await LiveWinUiTargetHost.CreateAsync();
    WindowsWhisperTargetInspector inspector = new();
    WindowsWhisperTextDelivery delivery = new(inspector);
    WindowsWhisperVerifiedSubmitter submitter = new(inspector);

    await target.FocusEditorAsync();
    WhisperTargetSnapshot captured = await WaitForControlledWinUiTargetAsync(
        inspector,
        target.ProcessId);
    True(captured.Context.Kind is WhisperTargetKind.PlainText or WhisperTargetKind.RichText);
    True(captured.Context.IsEditable);
    False(captured.Context.IsElevated);
    True(string.Equals(
        "Soltex.Whisper.WinUiTarget",
        captured.Context.ProcessName,
        StringComparison.OrdinalIgnoreCase));

    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        LiveWinUiTargetHost.ProbeText,
        WhisperSubmitOrigin.DedicatedShortcut,
        RestoreClipboard: true,
        "owner-controlled WinUI matrix");
    Stopwatch insertionTimer = Stopwatch.StartNew();
    WhisperTextDeliveryResult insertion = await delivery.DeliverAsync(
        new WhisperTextDeliveryRequest(decision, captured),
        CancellationToken.None);
    insertionTimer.Stop();
    True(insertion.MutationDispatched);
    False(insertion.Copied);

    await target.FocusEditorAsync();
    Stopwatch submitTimer = Stopwatch.StartNew();
    WhisperVerifiedSubmitResult submission = await submitter.SubmitAsync(
        new WhisperVerifiedSubmitRequest(
            decision,
            captured,
            insertion,
            FirstUseWarningAccepted: true),
        CancellationToken.None);
    True(submission.Authorization.Allowed);
    True(submission.Verification.Verified);
    True(submission.EnterDispatched);
    await target.WaitForEnterAsync();
    submitTimer.Stop();
    Console.WriteLine(
        $"MEASURE whisper_winui_matrix category={captured.Context.Kind} " +
        $"framework={target.FrameworkId} method={insertion.Method} " +
        $"insert_ms={insertionTimer.Elapsed.TotalMilliseconds:F2} " +
        $"verified_submit_ms={submitTimer.Elapsed.TotalMilliseconds:F2} " +
        "enter_events=1 content_logged=0");
}

static async Task<WhisperTargetSnapshot> WaitForControlledWinUiTargetAsync(
    WindowsWhisperTargetInspector inspector,
    int processId)
{
    Stopwatch timeout = Stopwatch.StartNew();
    WhisperTargetSnapshot? lastSnapshot = null;
    while (timeout.Elapsed < TimeSpan.FromSeconds(10))
    {
        WhisperTargetSnapshot? snapshot = await inspector.InspectAsync(CancellationToken.None);
        lastSnapshot = snapshot ?? lastSnapshot;
        if (snapshot is { Context.IsEditable: true } &&
            snapshot.Identity.ProcessId == processId &&
            snapshot.Context.Kind is WhisperTargetKind.PlainText or WhisperTargetKind.RichText)
        {
            return snapshot;
        }

        await Task.Delay(100);
    }

    throw new InvalidOperationException(
        $"The controlled WinUI target did not become inspectable " +
        $"(failure={inspector.LastFailure}; expected_pid={processId}; " +
        $"observed_pid={lastSnapshot?.Identity.ProcessId}; " +
        $"category={lastSnapshot?.Context.Kind}; " +
        $"editable={lastSnapshot?.Context.IsEditable}).");
}

static async Task<WhisperTargetSnapshot> WaitForControlledTerminalTargetAsync(
    WindowsWhisperTargetInspector inspector)
{
    Stopwatch timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(10))
    {
        WhisperTargetSnapshot? snapshot = await inspector.InspectAsync(CancellationToken.None);
        if (snapshot is { Context.Kind: WhisperTargetKind.Terminal, Context.IsEditable: true })
        {
            return snapshot;
        }

        await Task.Delay(100);
    }

    throw new InvalidOperationException(
        $"The controlled Windows Terminal target did not become inspectable ({inspector.LastFailure}).");
}

static async Task<WhisperTargetSnapshot> WaitForControlledBrowserTargetAsync(
    WindowsWhisperTargetInspector inspector)
{
    Stopwatch timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(10))
    {
        WhisperTargetSnapshot? snapshot = await inspector.InspectAsync(CancellationToken.None);
        if (snapshot is { Context.Kind: WhisperTargetKind.Browser, Context.IsEditable: true })
        {
            return snapshot;
        }

        await Task.Delay(100);
    }

    throw new InvalidOperationException(
        $"The controlled Chromium input did not become inspectable ({inspector.LastFailure}).");
}

static WhisperTextDeliveryRequest MatrixDeliveryRequest(WhisperTargetSnapshot captured) => new(
    new WhisperDeliveryDecision(
        WhisperDeliveryKind.InsertText,
        "matrix replacement",
        WhisperSubmitOrigin.None,
        RestoreClipboard: true,
        "owner-controlled target matrix"),
    captured);

static void SendInjectedShortcut()
{
    NativeInput[] inputs =
    [
        NativeInput.Keyboard(0x11, keyUp: false),
        NativeInput.Keyboard(0x10, keyUp: false),
        NativeInput.Keyboard(0x87, keyUp: false),
        NativeInput.Keyboard(0x87, keyUp: true),
        NativeInput.Keyboard(0x10, keyUp: true),
        NativeInput.Keyboard(0x11, keyUp: true)
    ];
    uint sent = TestNativeMethods.SendInput(
        checked((uint)inputs.Length),
        inputs,
        Marshal.SizeOf<NativeInput>());
    if (sent != inputs.Length)
    {
        throw new InvalidOperationException("Windows did not accept the bounded shortcut input sequence.");
    }
}

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

static async Task LiveLocalModel()
{
    string audioPath = RequireLiveModelValue("SOLTEX_WHISPER_LIVE_AUDIO_PATH");
    string expectedAudioSha256 = RequireLiveModelValue(
        "SOLTEX_WHISPER_LIVE_AUDIO_SHA256");
    using WhisperAudioClip clip = await LoadLiveModelFixtureAsync(
        audioPath,
        expectedAudioSha256);

    ProductDataRootResolution resolution = ProductDataRootResolver.ResolveDefault();
    await using WindowsWhisperLocalModelManager manager = new(resolution.ProductRoot);
    WhisperModelStatus model = await manager.GetStatusAsync(CancellationToken.None);
    True(model.IsVerified);
    Equal(574_041_195L, model.InstalledBytes);

    await using WindowsWhisperLocalTranscriber transcriber = new(manager);
    using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
    using Process process = Process.GetCurrentProcess();
    process.Refresh();
    long workingSetBefore = process.WorkingSet64;
    Stopwatch transcription = Stopwatch.StartNew();
    string transcript = await transcriber.TranscribeAsync(
        clip,
        new WhisperTranscriptionContext(
            WhisperCaptureMode.PushToTalk,
            "en",
            "owner-controlled-live-model",
            "default",
            Array.Empty<string>()),
        timeout.Token);
    transcription.Stop();
    True(!string.IsNullOrWhiteSpace(transcript));

    WhisperLocalTranscriptionDiagnostic diagnostic = transcriber
        .CreateDiagnosticSnapshot()
        .Last();
    Equal(WhisperLocalTranscriptionResultCategory.Succeeded, diagnostic.Result);
    Equal(WhisperLocalTranscriptionFailureKind.None, diagnostic.Failure);
    Equal(WhisperLocalModelDefaults.ProviderId, diagnostic.ProviderId);
    Equal(WhisperLocalModelDefaults.ModelId, diagnostic.ModelId);
    Equal(WhisperLocalModelDefaults.RuntimeId, diagnostic.RuntimeId);

    Stopwatch unload = Stopwatch.StartNew();
    await transcriber.UnloadAsync(timeout.Token);
    unload.Stop();
    transcript = string.Empty;

    Stopwatch restart = Stopwatch.StartNew();
    string restartedTranscript = await transcriber.TranscribeAsync(
        clip,
        new WhisperTranscriptionContext(
            WhisperCaptureMode.PushToTalk,
            "en",
            "owner-controlled-live-model-restart",
            "default",
            Array.Empty<string>()),
        timeout.Token);
    restart.Stop();
    True(!string.IsNullOrWhiteSpace(restartedTranscript));
    restartedTranscript = string.Empty;
    await transcriber.UnloadAsync(timeout.Token);

    using CancellationTokenSource cancellationProbe =
        CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
    cancellationProbe.CancelAfter(TimeSpan.FromMilliseconds(100));
    Stopwatch cancellation = Stopwatch.StartNew();
    bool cancellationObserved = false;
    try
    {
        string canceledTranscript = await transcriber.TranscribeAsync(
            clip,
            new WhisperTranscriptionContext(
                WhisperCaptureMode.PushToTalk,
                "en",
                "owner-controlled-live-model-cancellation",
                "default",
                Array.Empty<string>()),
            cancellationProbe.Token);
        canceledTranscript = string.Empty;
    }
    catch (OperationCanceledException) when (cancellationProbe.IsCancellationRequested)
    {
        cancellationObserved = true;
    }

    cancellation.Stop();
    True(cancellationObserved);
    Stopwatch cancellationUnload = Stopwatch.StartNew();
    await transcriber.UnloadAsync(timeout.Token);
    cancellationUnload.Stop();
    process.Refresh();
    long workingSetAfter = process.WorkingSet64;
    long peakWorkingSet = process.PeakWorkingSet64;
    Console.WriteLine(
        $"MEASURE whisper_live_model duration_ms={clip.Duration.TotalMilliseconds:F1} " +
        $"transcription_ms={transcription.Elapsed.TotalMilliseconds:F1} " +
        $"unload_ms={unload.Elapsed.TotalMilliseconds:F1} " +
        $"restart_transcription_ms={restart.Elapsed.TotalMilliseconds:F1} " +
        $"cancellation_ms={cancellation.Elapsed.TotalMilliseconds:F1} " +
        $"cancellation_unload_ms={cancellationUnload.Elapsed.TotalMilliseconds:F1} " +
        $"working_set_before_bytes={workingSetBefore} " +
        $"working_set_after_bytes={workingSetAfter} " +
        $"peak_working_set_bytes={peakWorkingSet} " +
        "restart=succeeded cancellation=observed result=succeeded " +
        "audio_logged=false transcript_logged=false");
}

static Task LiveModelFixtureParsing()
{
    byte[] valid = CreateLiveModelWave();
    byte[] invalidRate = (byte[])valid.Clone();
    byte[] truncated = valid[..^1];
    try
    {
        using WhisperAudioClip clip = ParseLiveModelPcm16MonoWave(valid);
        Equal(16_000, clip.SampleRateHz);
        Equal(1, clip.ChannelCount);
        Equal(402, clip.Pcm16.Length);

        BinaryPrimitives.WriteUInt32LittleEndian(invalidRate.AsSpan(24), 44_100);
        Throws<InvalidDataException>(() => ParseLiveModelPcm16MonoWave(invalidRate));
        Throws<InvalidDataException>(() => ParseLiveModelPcm16MonoWave(truncated));
        return Task.CompletedTask;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(valid);
        CryptographicOperations.ZeroMemory(invalidRate);
        CryptographicOperations.ZeroMemory(truncated);
    }
}

static byte[] CreateLiveModelWave()
{
    const int pcmBytes = 402;
    byte[] wave = new byte[44 + pcmBytes];
    "RIFF"u8.CopyTo(wave);
    BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), checked((uint)(wave.Length - 8)));
    "WAVE"u8.CopyTo(wave.AsSpan(8));
    "fmt "u8.CopyTo(wave.AsSpan(12));
    BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(16), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(20), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(22), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(24), 16_000);
    BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(28), 32_000);
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(32), sizeof(short));
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(34), 16);
    "data"u8.CopyTo(wave.AsSpan(36));
    BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(40), pcmBytes);
    return wave;
}

static string RequireLiveModelValue(string variableName)
{
    string? value = Environment.GetEnvironmentVariable(variableName);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"{variableName} must be set for the owner-host live-model proof.");
    }

    return value.Trim();
}

static async ValueTask<WhisperAudioClip> LoadLiveModelFixtureAsync(
    string configuredPath,
    string expectedSha256)
{
    const int maximumFixtureBytes = 8 * 1_024 * 1_024;
    string path = Path.GetFullPath(configuredPath);
    if (!File.Exists(path) ||
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
    {
        throw new InvalidOperationException(
            "The owner-selected live-model audio fixture is unavailable or unsafe.");
    }

    await using FileStream stream = new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1_024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (stream.Length is < 44 or > maximumFixtureBytes)
    {
        throw new InvalidDataException(
            "The owner-selected live-model audio fixture is outside its byte bound.");
    }

    byte[] wave = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
    try
    {
        await stream.ReadExactlyAsync(wave, CancellationToken.None);
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "The live-model audio fixture digest is invalid.",
                exception);
        }

        byte[] observed = SHA256.HashData(wave);
        try
        {
            if (expected.Length != observed.Length ||
                !CryptographicOperations.FixedTimeEquals(expected, observed))
            {
                throw new InvalidDataException(
                    "The live-model audio fixture did not match its owner-pinned digest.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(observed);
        }

        return ParseLiveModelPcm16MonoWave(wave);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(wave);
    }
}

static WhisperAudioClip ParseLiveModelPcm16MonoWave(ReadOnlySpan<byte> wave)
{
    if (wave.Length < 44 ||
        !wave[..4].SequenceEqual("RIFF"u8) ||
        !wave.Slice(8, 4).SequenceEqual("WAVE"u8))
    {
        throw new InvalidDataException(
            "The live-model fixture is not a bounded RIFF WAVE file.");
    }

    ushort format = 0;
    ushort channels = 0;
    uint sampleRate = 0;
    ushort blockAlign = 0;
    ushort bitsPerSample = 0;
    int dataOffset = -1;
    int dataLength = -1;
    int offset = 12;
    while (offset <= wave.Length - 8)
    {
        ReadOnlySpan<byte> id = wave.Slice(offset, 4);
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(
            wave.Slice(offset + 4, sizeof(uint)));
        if (declaredLength > int.MaxValue)
        {
            throw new InvalidDataException(
                "The live-model fixture contains an oversized WAVE chunk.");
        }

        int chunkLength = checked((int)declaredLength);
        int contentOffset = checked(offset + 8);
        int contentEnd = checked(contentOffset + chunkLength);
        if (contentEnd > wave.Length)
        {
            throw new InvalidDataException(
                "The live-model fixture contains a truncated WAVE chunk.");
        }

        if (id.SequenceEqual("fmt "u8))
        {
            if (chunkLength < 16)
            {
                throw new InvalidDataException(
                    "The live-model fixture has an incomplete format chunk.");
            }

            ReadOnlySpan<byte> value = wave.Slice(contentOffset, chunkLength);
            format = BinaryPrimitives.ReadUInt16LittleEndian(value);
            channels = BinaryPrimitives.ReadUInt16LittleEndian(value[2..]);
            sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
            blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(value[12..]);
            bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(value[14..]);
        }
        else if (id.SequenceEqual("data"u8))
        {
            if (dataOffset >= 0)
            {
                throw new InvalidDataException(
                    "The live-model fixture contains multiple audio data chunks.");
            }

            dataOffset = contentOffset;
            dataLength = chunkLength;
        }

        offset = checked(contentEnd + (chunkLength & 1));
    }

    if (format != 1 || channels != 1 || sampleRate != 16_000 ||
        blockAlign != sizeof(short) || bitsPerSample != 16 ||
        dataOffset < 0 || dataLength < sizeof(short) ||
        dataLength % sizeof(short) != 0)
    {
        throw new InvalidDataException(
            "The live-model fixture must contain 16 kHz mono PCM16 audio.");
    }

    byte[] pcm = wave.Slice(dataOffset, dataLength).ToArray();
    TimeSpan duration = TimeSpan.FromSeconds(
        dataLength / (double)(sampleRate * blockAlign));
    return WhisperAudioClip.CreateOwned(pcm, checked((int)sampleRate), channels, duration);
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

static async Task HistoryRetentionAdapter()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "Soltex.Whisper.Windows.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        await using WindowsWhisperHistoryRetentionStore store = new(root);
        WhisperHistoryEntry expected = new(
            DateTimeOffset.UtcNow,
            "notepad",
            WhisperDeliveryKind.InsertText,
            "adapter fixture");
        await store.SaveAsync([expected], 7, CancellationToken.None);
        IReadOnlyList<WhisperHistoryEntry> loaded = await store.LoadAsync(
            7,
            CancellationToken.None);
        Equal(1, loaded.Count);
        Equal(expected, loaded[0]);

        await store.RewriteAsync([], 7, CancellationToken.None);
        Equal(0, (await store.LoadAsync(7, CancellationToken.None)).Count);
        False(File.Exists(Path.Combine(root, "whisper-history.json")));
        False(File.Exists(Path.Combine(root, "whisper-history.json.bak")));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ProviderCredentialBoundary()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "soltex-whisper-credential-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    byte[] credential = "owner-controlled-provider-secret"u8.ToArray();
    try
    {
        await using WindowsWhisperCredentialStore store = new(root, "Example.Provider");
        Equal("example.provider", store.ProviderId);
        False(await store.IsAvailableAsync(CancellationToken.None));

        await store.SaveAsync(credential, CancellationToken.None);
        True(await store.IsAvailableAsync(CancellationToken.None));
        WhisperCredentialLease lease = await store.AcquireAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("Saved credential was unavailable.");
        ReadOnlyMemory<byte> observed = lease.Bytes;
        True(observed.Span.SequenceEqual(credential));
        lease.Dispose();
        True(observed.Span.ToArray().All(value => value == 0));

        string allState = string.Join(
            "\n",
            Directory.EnumerateFiles(root).Select(File.ReadAllText));
        False(allState.Contains(
            "owner-controlled-provider-secret",
            StringComparison.Ordinal));

        await store.DeleteAsync(CancellationToken.None);
        False(await store.IsAvailableAsync(CancellationToken.None));
        False(Directory.EnumerateFiles(root, "whisper-credential-*", SearchOption.TopDirectoryOnly).Any());
    }
    finally
    {
        CryptographicOperations.ZeroMemory(credential);
        Directory.Delete(root, recursive: true);
    }

    Throws<ArgumentException>(() =>
        _ = new WindowsWhisperCredentialStore(Path.GetTempPath(), "invalid/provider"));
}

static async Task UninstallCleanupIsExact()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "soltex-whisper-uninstall-tests-" + Guid.NewGuid().ToString("N"));
    string modelsRoot = Path.Combine(root, "whisper", "models");
    string stateRoot = Path.Combine(root, "state");
    Directory.CreateDirectory(modelsRoot);
    Directory.CreateDirectory(stateRoot);
    string modelPath = Path.Combine(
        modelsRoot,
        WhisperLocalModelArtifact.TurboQ5Cpu.FileName);
    string partialPath = modelPath + "." + Guid.NewGuid().ToString("N") + ".partial";
    string unrelatedModelFile = Path.Combine(modelsRoot, "owner-note.txt");
    string settingsPath = Path.Combine(root, "whisper-settings.json");
    string settingsTemporary = settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
    string unrelatedTemporary = settingsPath + ".tmp-owner";
    byte[] credential = "cleanup-fixture"u8.ToArray();
    try
    {
        File.WriteAllBytes(modelPath, [1, 2, 3]);
        File.WriteAllBytes(partialPath, [4, 5, 6]);
        File.WriteAllText(Path.Combine(modelsRoot, ".install.lock"), string.Empty);
        File.WriteAllText(unrelatedModelFile, "preserve");
        File.WriteAllText(settingsPath, "{}");
        File.WriteAllText(settingsTemporary, "temporary");
        File.WriteAllText(unrelatedTemporary, "preserve");

        await using (WindowsWhisperHistoryRetentionStore history = new(stateRoot))
        {
            await history.SaveAsync(
                [new WhisperHistoryEntry(
                    DateTimeOffset.UtcNow,
                    "sample",
                    WhisperDeliveryKind.InsertText,
                    "private fixture")],
                7,
                CancellationToken.None);
        }

        await using (WindowsWhisperCredentialStore secret = new(
                         stateRoot,
                         WhisperLocalModelDefaults.ProviderId))
        {
            await secret.SaveAsync(credential, CancellationToken.None);
        }

        WhisperUninstallCleanupResult result =
            await WindowsWhisperUninstallCleanup.CleanAsync(
                root,
                CancellationToken.None);
        True(result.ModelArtifactRemoved);
        Equal(1, result.InterruptedDownloadsRemoved);
        True(result.InstallLockRemoved);
        True(result.SettingsRemoved);
        Equal(1, result.SettingsTemporaryArtifactsRemoved);
        True(result.EncryptedHistoryRemoved);
        True(result.ProviderCredentialRemoved);

        False(File.Exists(modelPath));
        False(File.Exists(partialPath));
        False(File.Exists(settingsPath));
        False(File.Exists(settingsTemporary));
        False(Directory.EnumerateFiles(
            stateRoot,
            "whisper-history*",
            SearchOption.TopDirectoryOnly).Any());
        False(Directory.EnumerateFiles(
            stateRoot,
            "whisper-credential-*",
            SearchOption.TopDirectoryOnly).Any());
        True(File.Exists(Path.Combine(stateRoot, "state.key")));
        True(File.Exists(unrelatedModelFile));
        True(File.Exists(unrelatedTemporary));
    }
    finally
    {
        CryptographicOperations.ZeroMemory(credential);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task LocalModelInstall()
{
    byte[] model = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(model))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(
            root,
            client,
            artifact);
        List<WhisperModelInstallProgress> progress = [];
        WhisperModelStatus installed = await manager.InstallAsync(
            new InlineProgress<WhisperModelInstallProgress>(progress.Add),
            CancellationToken.None);

        True(installed.IsInstalled);
        True(installed.IsVerified);
        Equal(artifact.ExpectedBytes, installed.InstalledBytes);
        Equal(1d, progress[^1].Fraction);
        Equal(artifact.UpstreamRevision, WindowsWhisperLocalModelManager.PinnedUpstreamRevision);
        True(File.ReadAllBytes(ModelPath(root, artifact)).SequenceEqual(model));
        True((await manager.GetStatusAsync(CancellationToken.None)).IsVerified);
    }
    finally
    {
        DeleteModelRoot(root);
        CryptographicOperations.ZeroMemory(model);
    }
}

static async Task LocalModelOversize()
{
    byte[] expected = Enumerable.Repeat((byte)0x2A, 256).ToArray();
    byte[] oversized = [.. expected, 0x7F];
    WhisperLocalModelArtifact artifact = TestModelArtifact(expected);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(oversized)),
        (_, _) => Task.FromResult(ModelResponse(oversized, artifact.ExpectedBytes))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        WhisperModelInstallException declared = await ThrowsValueAsync<WhisperModelInstallException>(
            async () => await manager.InstallAsync(null, CancellationToken.None));
        Equal(WhisperModelFailureKind.SizeMismatch, declared.Kind);
        WhisperModelInstallException streamed = await ThrowsValueAsync<WhisperModelInstallException>(
            async () => await manager.InstallAsync(null, CancellationToken.None));
        Equal(WhisperModelFailureKind.SizeMismatch, streamed.Kind);
        False(File.Exists(ModelPath(root, artifact)));
        False(OwnedPartials(root, artifact).Any());
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelDigestMismatch()
{
    byte[] expected = Enumerable.Repeat((byte)0x11, 512).ToArray();
    byte[] changed = Enumerable.Repeat((byte)0x22, expected.Length).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(expected);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(changed))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        WhisperModelInstallException failure = await ThrowsValueAsync<WhisperModelInstallException>(
            async () => await manager.InstallAsync(null, CancellationToken.None));
        Equal(WhisperModelFailureKind.DigestMismatch, failure.Kind);
        False(File.Exists(ModelPath(root, artifact)));
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelTruncationAndChange()
{
    byte[] model = Enumerable.Range(0, 1024).Select(index => (byte)(index % 239)).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    ScriptedModelHttpHandler handler = new(
        (_, _) => Task.FromResult(ModelResponse(model[..^1], artifact.ExpectedBytes)),
        (_, _) => Task.FromResult(ModelResponse(model)));
    using HttpClient client = new(handler);
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        WhisperModelInstallException truncated = await ThrowsValueAsync<WhisperModelInstallException>(
            async () => await manager.InstallAsync(null, CancellationToken.None));
        Equal(WhisperModelFailureKind.SizeMismatch, truncated.Kind);

        WhisperModelStatus installed = await manager.InstallAsync(null, CancellationToken.None);
        True(installed.IsVerified);
        byte[] changed = model.ToArray();
        changed[^1] ^= 0xFF;
        File.WriteAllBytes(ModelPath(root, artifact), changed);
        WhisperModelStatus invalid = await manager.GetStatusAsync(CancellationToken.None);
        Equal(WhisperModelInstallState.Invalid, invalid.State);
        Equal(WhisperModelFailureKind.DigestMismatch, invalid.FailureKind);
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelCancellationCleanup()
{
    byte[] model = Enumerable.Repeat((byte)0x5C, 8192).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(model))));
    using CancellationTokenSource cancellation = new();
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        InlineProgress<WhisperModelInstallProgress> progress = new(value =>
        {
            if (value.ReceivedBytes == value.ExpectedBytes)
            {
                cancellation.Cancel();
            }
        });
        await ThrowsAsync<OperationCanceledException>(async () =>
            await manager.InstallAsync(progress, cancellation.Token));
        False(File.Exists(ModelPath(root, artifact)));
        False(OwnedPartials(root, artifact).Any());
        Equal(
            WhisperModelFailureKind.Cancelled,
            (await manager.GetStatusAsync(CancellationToken.None)).FailureKind);
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelConcurrentInstall()
{
    byte[] model = Enumerable.Repeat((byte)0x6D, 2048).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    BlockingModelHttpHandler handler = new(model);
    using HttpClient client = new(handler);
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        Task<WhisperModelStatus> first = manager
            .InstallAsync(null, CancellationToken.None)
            .AsTask();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        WhisperModelInstallException busy = await ThrowsValueAsync<WhisperModelInstallException>(
            async () => await manager.InstallAsync(null, CancellationToken.None));
        Equal(WhisperModelFailureKind.Busy, busy.Kind);
        handler.Release.TrySetResult();
        True((await first).IsVerified);
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelRepair()
{
    byte[] model = Enumerable.Range(0, 1536).Select(index => (byte)(index % 197)).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    ScriptedModelHttpHandler handler = new(
        (_, _) => Task.FromResult(ModelResponse(model)),
        (_, _) => Task.FromResult(ModelResponse(model)));
    using HttpClient client = new(handler);
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        _ = await manager.InstallAsync(null, CancellationToken.None);
        byte[] invalid = Enumerable.Repeat((byte)0xA5, model.Length).ToArray();
        File.WriteAllBytes(ModelPath(root, artifact), invalid);
        Equal(
            WhisperModelInstallState.Invalid,
            (await manager.GetStatusAsync(CancellationToken.None)).State);

        WhisperModelStatus repaired = await manager.RepairAsync(null, CancellationToken.None);
        True(repaired.IsVerified);
        True(File.ReadAllBytes(ModelPath(root, artifact)).SequenceEqual(model));
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelOwnedDeletion()
{
    byte[] model = Enumerable.Repeat((byte)0x3E, 768).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(model))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        _ = await manager.InstallAsync(null, CancellationToken.None);
        string unrelated = Path.Combine(root, "whisper", "models", "owner-note.txt");
        File.WriteAllText(unrelated, "preserve");

        await manager.DeleteAsync(CancellationToken.None);
        False(File.Exists(ModelPath(root, artifact)));
        True(File.Exists(unrelated));
        Equal(
            WhisperModelInstallState.NotInstalled,
            (await manager.GetStatusAsync(CancellationToken.None)).State);
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelInterruptedCleanup()
{
    byte[] model = Enumerable.Repeat((byte)0x4F, 640).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(model))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        string interrupted = Path.Combine(
            root,
            "whisper",
            "models",
            artifact.FileName + ".0123456789abcdef0123456789abcdef.partial");
        File.WriteAllBytes(interrupted, [1, 2, 3]);
        string unrelated = Path.Combine(
            root,
            "whisper",
            "models",
            artifact.FileName + ".not-owned.partial");
        File.WriteAllBytes(unrelated, [4, 5, 6]);

        True((await manager.InstallAsync(null, CancellationToken.None)).IsVerified);
        False(File.Exists(interrupted));
        True(File.Exists(unrelated));
    }
    finally
    {
        DeleteModelRoot(root);
    }
}

static async Task LocalModelVerifiedLease()
{
    byte[] model = Enumerable.Range(0, 1024).Select(index => (byte)(index % 211)).ToArray();
    WhisperLocalModelArtifact artifact = TestModelArtifact(model);
    using HttpClient client = new(new ScriptedModelHttpHandler(
        (_, _) => Task.FromResult(ModelResponse(model))));
    string root = CreateModelRoot();
    try
    {
        await using WindowsWhisperLocalModelManager manager = new(root, client, artifact);
        True((await manager.InstallAsync(null, CancellationToken.None)).IsVerified);
        IWhisperLocalModelSource source = manager;
        await using (WhisperVerifiedModelLease lease =
                     await source.OpenVerifiedAsync(CancellationToken.None))
        {
            Equal(ModelPath(root, artifact), lease.ModelPath);
            Throws<IOException>(() => File.WriteAllBytes(lease.ModelPath, model));
        }

        File.WriteAllBytes(ModelPath(root, artifact), model);
        True((await manager.GetStatusAsync(CancellationToken.None)).IsVerified);
    }
    finally
    {
        DeleteModelRoot(root);
        CryptographicOperations.ZeroMemory(model);
    }
}

static Task PcmWaveProjection()
{
    byte[] pcm = Enumerable.Range(0, 640).Select(index => (byte)(index % 251)).ToArray();
    using WhisperPcmWaveStream stream = new(pcm, 16_000, 1);
    pcm[0] = 0x7D;
    byte[] wave = new byte[checked((int)stream.Length)];
    stream.ReadExactly(wave);

    Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
    Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
    Equal("fmt ", System.Text.Encoding.ASCII.GetString(wave, 12, 4));
    Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(20, 2)));
    Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(22, 2)));
    Equal(16_000u, BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(24, 4)));
    Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(34, 2)));
    Equal("data", System.Text.Encoding.ASCII.GetString(wave, 36, 4));
    Equal((uint)pcm.Length, BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(40, 4)));
    True(wave.AsSpan(WhisperPcmWaveStream.HeaderBytes).SequenceEqual(pcm));
    Equal((byte)0x7D, wave[WhisperPcmWaveStream.HeaderBytes]);
    return Task.CompletedTask;
}

static async Task LocalTranscriberSuccess()
{
    await using TestLocalModelSource model = new();
    ScriptedLocalRuntime runtime = new(
        LocalRuntimeScripts.Segments(" hello", " world "),
        LocalRuntimeScripts.Segments("second"));
    ScriptedLocalRuntimeFactory factory = new(runtime);
    await using WindowsWhisperLocalTranscriber transcriber = new(model, factory);
    byte[] owned = CreateTranscriberPcm();
    using WhisperAudioClip clip = WhisperAudioClip.CreateOwned(
        owned,
        16_000,
        1,
        TimeSpan.FromMilliseconds(20));
    WhisperTranscriptionContext context = new(
        WhisperCaptureMode.PushToTalk,
        "en-US",
        "notepad",
        "Message",
        ["Soltex", "Aether Foundry"]);

    Equal("hello world", await transcriber.TranscribeAsync(
        clip,
        context,
        CancellationToken.None));
    Equal("second", await transcriber.TranscribeAsync(
        clip,
        context,
        CancellationToken.None));
    Equal(1, factory.CreateCount);
    Equal(1, model.OpenCount);
    Equal(2, runtime.ObservedOptions.Count);
    Equal("en", runtime.ObservedOptions[0].Language);
    True(runtime.ObservedOptions[0].Prompt?.Contains("Soltex", StringComparison.Ordinal) == true);
    True(runtime.ObservedOptions[0].Prompt?.Contains("Aether Foundry", StringComparison.Ordinal) == true);
    True(runtime.ObservedWaveFiles.All(wave =>
        wave.AsSpan(WhisperPcmWaveStream.HeaderBytes).SequenceEqual(owned)));
    True(transcriber.CreateDiagnosticSnapshot().All(item =>
        item.Result == WhisperLocalTranscriptionResultCategory.Succeeded &&
        item.Failure == WhisperLocalTranscriptionFailureKind.None));
}

static Task LocalRuntimePackageProbe()
{
    WhisperLocalRuntimeProbeResult result = WhisperLocalRuntimeProbe.Run();
    Equal(WhisperLocalModelDefaults.ProviderId, result.ProviderId);
    Equal(WhisperLocalModelDefaults.RuntimeId, result.RuntimeId);
    True(result.Available);
    return Task.CompletedTask;
}

static async Task LocalTranscriberFailureRecovery()
{
    const string privateText = "owner-secret transcript C:\\private\\note.txt";
    await using TestLocalModelSource model = new();
    ScriptedLocalRuntime failing = new(LocalRuntimeScripts.Fault(privateText));
    ScriptedLocalRuntime recovered = new(LocalRuntimeScripts.Segments("recovered"));
    ScriptedLocalRuntimeFactory factory = new(failing, recovered);
    await using WindowsWhisperLocalTranscriber transcriber = new(model, factory);
    using WhisperAudioClip clip = CreateTranscriberClip();

    WhisperLocalTranscriptionException failure =
        await ThrowsValueAsync<WhisperLocalTranscriptionException>(async () =>
            await transcriber.TranscribeAsync(
                clip,
                DefaultTranscriptionContext(),
                CancellationToken.None));
    Equal(WhisperLocalTranscriptionFailureKind.RuntimeFault, failure.Kind);
    False(failure.ToString().Contains("owner-secret", StringComparison.Ordinal));
    False(failure.ToString().Contains("private", StringComparison.Ordinal));
    string evidence = string.Join(
        "\n",
        transcriber.CreateDiagnosticSnapshot().Select(item => item.ToString()));
    False(evidence.Contains("owner-secret", StringComparison.Ordinal));
    False(evidence.Contains("private", StringComparison.Ordinal));

    Equal("recovered", await transcriber.TranscribeAsync(
        clip,
        DefaultTranscriptionContext(),
        CancellationToken.None));
    Equal(2, factory.CreateCount);
    True(failing.IsDisposed);
}

static async Task LocalTranscriberCancellation()
{
    await using TestLocalModelSource model = new();
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ScriptedLocalRuntime runtime = new(LocalRuntimeScripts.Block(started));
    ScriptedLocalRuntimeFactory factory = new(runtime);
    await using WindowsWhisperLocalTranscriber transcriber = new(model, factory);
    using WhisperAudioClip clip = CreateTranscriberClip();
    using CancellationTokenSource cancellation = new();

    Task<string> work = transcriber.TranscribeAsync(
        clip,
        DefaultTranscriptionContext(),
        cancellation.Token).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(async () => await work);
    Equal(
        WhisperLocalTranscriptionResultCategory.Cancelled,
        transcriber.CreateDiagnosticSnapshot()[^1].Result);
}

static async Task LocalTranscriberBounds()
{
    await using TestLocalModelSource model = new();
    ScriptedLocalRuntimeFactory shortAudioFactory = new(
        new ScriptedLocalRuntime(LocalRuntimeScripts.Segments("unused")));
    await using (WindowsWhisperLocalTranscriber transcriber =
                 new(model, shortAudioFactory))
    {
        using WhisperAudioClip shortClip = WhisperAudioClip.CreateOwned(
            new byte[WindowsWhisperLocalTranscriber.MinimumPcmSampleCount * sizeof(short) - 2],
            16_000,
            1,
            TimeSpan.FromMilliseconds(10));
        WhisperLocalTranscriptionException invalid =
            await ThrowsValueAsync<WhisperLocalTranscriptionException>(async () =>
                await transcriber.TranscribeAsync(
                    shortClip,
                    DefaultTranscriptionContext(),
                    CancellationToken.None));
        Equal(WhisperLocalTranscriptionFailureKind.InvalidAudio, invalid.Kind);
        Equal(0, shortAudioFactory.CreateCount);
    }

    ScriptedLocalRuntimeFactory resultFactory = new(
        new ScriptedLocalRuntime(LocalRuntimeScripts.Segments("   ")),
        new ScriptedLocalRuntime(LocalRuntimeScripts.Segments(
            new string('a', WhisperLimits.MaximumTranscriptCharacters),
            "b")));
    await using WindowsWhisperLocalTranscriber bounded = new(model, resultFactory);
    using WhisperAudioClip clip = CreateTranscriberClip();
    WhisperLocalTranscriptionException empty =
        await ThrowsValueAsync<WhisperLocalTranscriptionException>(async () =>
            await bounded.TranscribeAsync(
                clip,
                DefaultTranscriptionContext(),
                CancellationToken.None));
    Equal(WhisperLocalTranscriptionFailureKind.EmptyResult, empty.Kind);
    await bounded.UnloadAsync();
    WhisperLocalTranscriptionException oversized =
        await ThrowsValueAsync<WhisperLocalTranscriptionException>(async () =>
            await bounded.TranscribeAsync(
                clip,
                DefaultTranscriptionContext(),
                CancellationToken.None));
    Equal(WhisperLocalTranscriptionFailureKind.OutputTooLarge, oversized.Kind);
}

static async Task LocalTranscriberUnload()
{
    await using TestLocalModelSource model = new();
    ScriptedLocalRuntime first = new(LocalRuntimeScripts.Segments("first"));
    ScriptedLocalRuntime second = new(LocalRuntimeScripts.Segments("second"));
    ScriptedLocalRuntimeFactory factory = new(first, second);
    await using WindowsWhisperLocalTranscriber transcriber = new(model, factory);
    byte[] owned = CreateTranscriberPcm();
    WhisperAudioClip clip = WhisperAudioClip.CreateOwned(
        owned,
        16_000,
        1,
        TimeSpan.FromMilliseconds(20));
    try
    {
        Equal("first", await transcriber.TranscribeAsync(
            clip,
            DefaultTranscriptionContext(),
            CancellationToken.None));
        await transcriber.UnloadAsync();
        True(first.IsDisposed);
        Equal("second", await transcriber.TranscribeAsync(
            clip,
            DefaultTranscriptionContext(),
            CancellationToken.None));
        Equal(2, factory.CreateCount);
    }
    finally
    {
        clip.Dispose();
    }

    True(owned.All(value => value == 0));
}

static WhisperAudioClip CreateTranscriberClip()
{
    return WhisperAudioClip.CreateOwned(
        CreateTranscriberPcm(),
        16_000,
        1,
        TimeSpan.FromMilliseconds(20));
}

static byte[] CreateTranscriberPcm()
{
    byte[] pcm = new byte[640];
    for (int index = 0; index < pcm.Length; index += sizeof(short))
    {
        pcm[index] = (byte)(index % 251);
        pcm[index + 1] = 0x20;
    }

    return pcm;
}

static WhisperTranscriptionContext DefaultTranscriptionContext() => new(
    Mode: WhisperCaptureMode.PushToTalk,
    PreferredLanguage: null,
    ProcessName: "notepad",
    StyleName: "Message",
    DictionaryTerms: Array.Empty<string>());

static WhisperLocalModelArtifact TestModelArtifact(byte[] model)
{
    string sha256 = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();
    return new WhisperLocalModelArtifact(
        WhisperLocalModelDefaults.ProviderId,
        WhisperLocalModelDefaults.ModelId,
        WhisperLocalModelDefaults.RuntimeId,
        "test-local-model.bin",
        new Uri("https://models.example.test/test-local-model.bin"),
        WindowsWhisperLocalModelManager.PinnedUpstreamRevision,
        model.LongLength,
        sha256);
}

static HttpResponseMessage ModelResponse(byte[] content, long? declaredLength = null)
{
    ByteArrayContent body = new(content);
    if (declaredLength is not null)
    {
        body.Headers.ContentLength = declaredLength.Value;
    }

    return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
}

static string CreateModelRoot()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "Soltex.Whisper.Model.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return Path.GetFullPath(root);
}

static string ModelPath(string root, WhisperLocalModelArtifact artifact) =>
    Path.Combine(root, "whisper", "models", artifact.FileName);

static IEnumerable<string> OwnedPartials(
    string root,
    WhisperLocalModelArtifact artifact) => Directory.Exists(Path.Combine(root, "whisper", "models"))
    ? Directory.EnumerateFiles(
        Path.Combine(root, "whisper", "models"),
        artifact.FileName + ".*.partial",
        SearchOption.TopDirectoryOnly)
    : [];

static void DeleteModelRoot(string root)
{
    string expectedParent = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "Soltex.Whisper.Model.Tests"));
    string fullRoot = Path.GetFullPath(root);
    if (!fullRoot.StartsWith(
            expectedParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The test model root escaped its expected parent.");
    }

    if (Directory.Exists(fullRoot))
    {
        Directory.Delete(fullRoot, recursive: true);
    }
}

static async Task<TException> ThrowsValueAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
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

internal sealed class TestLocalModelSource : IWhisperLocalModelSource, IAsyncDisposable
{
    private readonly string _root;
    private readonly string _modelPath;
    private int _openCount;
    private bool _disposed;

    internal TestLocalModelSource()
    {
        string parent = Path.Combine(
            Path.GetTempPath(),
            "Soltex.Whisper.LocalTranscriber.Tests");
        _root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _modelPath = Path.Combine(_root, "verified-test-model.bin");
        File.WriteAllBytes(_modelPath, [0x53, 0x4F, 0x4C, 0x54, 0x45, 0x58]);
    }

    internal int OpenCount => Volatile.Read(ref _openCount);

    public ValueTask<WhisperVerifiedModelLease> OpenVerifiedAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        FileStream ownershipHandle = new(
            _modelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4_096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            WindowsWhisperFileIdentity identity =
                WindowsWhisperFileIdentity.From(ownershipHandle.SafeFileHandle);
            _ = Interlocked.Increment(ref _openCount);
            return ValueTask.FromResult(
                new WhisperVerifiedModelLease(_modelPath, ownershipHandle, identity));
        }
        catch
        {
            ownershipHandle.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        string fullRoot = Path.GetFullPath(_root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "Soltex.Whisper.LocalTranscriber.Tests"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullRoot.StartsWith(
                expectedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The local-transcriber test root escaped.");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

internal delegate IAsyncEnumerable<string> LocalRuntimeScript(
    CancellationToken cancellationToken);

internal sealed class ScriptedLocalRuntimeFactory : IWhisperLocalRuntimeFactory
{
    private readonly ConcurrentQueue<IWhisperLocalRuntime> _runtimes;
    private int _createCount;

    internal ScriptedLocalRuntimeFactory(params IWhisperLocalRuntime[] runtimes)
    {
        _runtimes = new ConcurrentQueue<IWhisperLocalRuntime>(runtimes);
    }

    internal int CreateCount => Volatile.Read(ref _createCount);

    public ValueTask<IWhisperLocalRuntime> CreateAsync(
        WhisperVerifiedModelLease model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_runtimes.TryDequeue(out IWhisperLocalRuntime? runtime))
        {
            throw new InvalidOperationException("No scripted local runtime remains.");
        }

        _ = Interlocked.Increment(ref _createCount);
        return ValueTask.FromResult(runtime);
    }
}

internal sealed class ScriptedLocalRuntime : IWhisperLocalRuntime
{
    private readonly ConcurrentQueue<LocalRuntimeScript> _scripts;

    internal ScriptedLocalRuntime(params LocalRuntimeScript[] scripts)
    {
        _scripts = new ConcurrentQueue<LocalRuntimeScript>(scripts);
    }

    internal List<WhisperLocalRuntimeOptions> ObservedOptions { get; } = [];

    internal List<byte[]> ObservedWaveFiles { get; } = [];

    internal bool IsDisposed { get; private set; }

    public async IAsyncEnumerable<string> TranscribeAsync(
        Stream waveStream,
        WhisperLocalRuntimeOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_scripts.TryDequeue(out LocalRuntimeScript? script))
        {
            throw new InvalidOperationException("No scripted transcription remains.");
        }

        using MemoryStream captured = new();
        await waveStream.CopyToAsync(captured, cancellationToken).ConfigureAwait(false);
        ObservedWaveFiles.Add(captured.ToArray());
        ObservedOptions.Add(options);

        await foreach (string segment in script(cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return segment;
        }
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

internal static class LocalRuntimeScripts
{
    internal static LocalRuntimeScript Segments(params string[] segments) =>
        cancellationToken => YieldSegments(segments, cancellationToken);

    internal static LocalRuntimeScript Fault(string message) =>
        cancellationToken => ThrowFailure(message, cancellationToken);

    internal static LocalRuntimeScript Block(TaskCompletionSource started) =>
        cancellationToken => WaitForCancellation(started, cancellationToken);

    private static async IAsyncEnumerable<string> YieldSegments(
        IReadOnlyList<string> segments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return segment;
        }
    }

    private static async IAsyncEnumerable<string> ThrowFailure(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        throw new InvalidOperationException(message);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<string> WaitForCancellation(
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        yield break;
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

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class ScriptedModelHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses;

    internal ScriptedModelHttpHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
    {
        _responses = new ConcurrentQueue<
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("No scripted model response remained.");
        }

        return response(request, cancellationToken);
    }
}

internal sealed class BlockingModelHttpHandler(byte[] content) : HttpMessageHandler
{
    internal TaskCompletionSource Started { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource Release { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _ = request;
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return ModelResponseForHandler(content);
    }

    private static HttpResponseMessage ModelResponseForHandler(byte[] model) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(model) };
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

internal sealed class FakeShortcutRegistrationFactory :
    IWhisperShortcutRegistrationFactory
{
    internal List<FakeShortcutRegistration> Created { get; } = [];

    internal bool FailNext { get; set; }

    public ValueTask<IWhisperShortcutRegistration> CreateAsync(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        Action<string> faultHandler,
        CancellationToken cancellationToken)
    {
        _ = shortcutSet;
        _ = faultHandler;
        cancellationToken.ThrowIfCancellationRequested();
        if (FailNext)
        {
            FailNext = false;
            throw new WhisperShortcutRegistrationException(
                "Scripted replacement registration failure.");
        }

        FakeShortcutRegistration registration = new(handler);
        Created.Add(registration);
        return ValueTask.FromResult<IWhisperShortcutRegistration>(registration);
    }
}

internal sealed class FakeShortcutRegistration(
    Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler) :
    IWhisperShortcutRegistration
{
    internal bool Active { get; private set; }

    public bool IsActive => Active;

    public WhisperShortcutPerformanceSnapshot Performance { get; } =
        new(0, 0, 0, 0);

    public void Activate() => Active = true;

    public void Deactivate() => Active = false;

    internal ValueTask EmitAsync(WhisperShortcutSignal signal)
    {
        if (!Active)
        {
            throw new InvalidOperationException("The fake registration is not active.");
        }

        return handler(signal, CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        Active = false;
        return ValueTask.CompletedTask;
    }
}

internal sealed class ScriptedTargetInspectionBackend(
    Func<WindowsWhisperTargetObservation> inspect) : IWhisperTargetInspectionBackend
{
    public WindowsWhisperTargetObservation InspectFocused() => inspect();
}

internal sealed class ScriptedTargetTextBackend(
    Func<WindowsWhisperTargetReadObservation?> read) :
    IWindowsWhisperTargetTextBackend
{
    public WindowsWhisperTargetReadObservation? ReadFocused(
        WhisperTargetSnapshot expectedTarget)
    {
        ArgumentNullException.ThrowIfNull(expectedTarget);
        return read();
    }
}

internal sealed class CancelingTargetInspector(
    WhisperTargetSnapshot first,
    CancellationTokenSource cancellation) : IWhisperTargetInspector
{
    private int _calls;

    public ValueTask<WhisperTargetSnapshot?> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _calls) == 1)
        {
            return ValueTask.FromResult<WhisperTargetSnapshot?>(first);
        }

        cancellation.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        throw new UnreachableException();
    }
}

internal sealed class FakeTargetTextReader(string? text) : IWhisperTargetTextReader
{
    internal int ReadCount { get; private set; }

    public ValueTask<WhisperTargetReadback?> ReadAsync(
        WhisperTargetSnapshot expectedTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedTarget);
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return ValueTask.FromResult(
            text is null
                ? null
                : new WhisperTargetReadback(
                    text,
                    WhisperVerificationMethod.AutomationTextRead));
    }
}

internal sealed class FakeSubmitPlatform(bool dispatchResult) :
    IWindowsWhisperSubmitPlatform
{
    internal int DispatchCount { get; private set; }

    internal bool FirstConsume { get; private set; }

    internal bool SecondConsume { get; private set; }

    public ValueTask<bool> TryEmitEnterAsync(
        WhisperSubmitAuthorization authorization,
        WhisperTargetSnapshot target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        DispatchCount++;
        FirstConsume = authorization.TryConsume();
        SecondConsume = authorization.TryConsume();
        return ValueTask.FromResult(dispatchResult && FirstConsume);
    }
}

internal sealed class FakeInsertionPlatform : IWindowsWhisperInsertionPlatform
{
    internal bool DirectResult { get; init; }

    internal bool PasteResult { get; init; } = true;

    internal WhisperClipboardRestoreOutcome RestoreOutcome { get; init; } =
        WhisperClipboardRestoreOutcome.Restored;

    internal int DirectCount { get; private set; }

    internal int StageCount { get; private set; }

    internal int CopyCount { get; private set; }

    internal int PasteCount { get; private set; }

    internal FakeClipboardLease? LastLease { get; private set; }

    public ValueTask<bool> TryInsertDirectAsync(
        WhisperTargetSnapshot capturedTarget,
        string text,
        CancellationToken cancellationToken)
    {
        _ = capturedTarget;
        _ = text;
        cancellationToken.ThrowIfCancellationRequested();
        DirectCount++;
        return ValueTask.FromResult(DirectResult);
    }

    public ValueTask<IWindowsWhisperClipboardLease> StageClipboardAsync(
        string text,
        bool capturePrevious,
        CancellationToken cancellationToken)
    {
        _ = text;
        _ = capturePrevious;
        cancellationToken.ThrowIfCancellationRequested();
        StageCount++;
        LastLease = new FakeClipboardLease(RestoreOutcome);
        return ValueTask.FromResult<IWindowsWhisperClipboardLease>(LastLease);
    }

    public ValueTask CopyAsync(string text, CancellationToken cancellationToken)
    {
        _ = text;
        cancellationToken.ThrowIfCancellationRequested();
        CopyCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> PasteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PasteCount++;
        return ValueTask.FromResult(PasteResult);
    }
}

internal sealed class FakeClipboardLease(WhisperClipboardRestoreOutcome restoreOutcome) :
    IWindowsWhisperClipboardLease
{
    internal int RestoreCount { get; private set; }

    public ValueTask<WhisperClipboardRestoreOutcome> TryRestoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoreCount++;
        return ValueTask.FromResult(restoreOutcome);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal enum WpfMatrixTarget
{
    TextBox,
    RichTextBox,
    PasswordBox,
    ReadOnlyTextBox,
    SecondTextBox
}

internal sealed class LiveWpfTargetMatrixHost : IAsyncDisposable
{
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private System.Windows.Window? _window;
    private System.Windows.Controls.TextBox? _textBox;
    private System.Windows.Controls.RichTextBox? _richTextBox;
    private System.Windows.Controls.PasswordBox? _passwordBox;
    private System.Windows.Controls.TextBox? _readOnlyTextBox;
    private System.Windows.Controls.TextBox? _secondTextBox;

    private LiveWpfTargetMatrixHost()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Soltex Whisper WPF target matrix"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal static async ValueTask<LiveWpfTargetMatrixHost> CreateAsync()
    {
        LiveWpfTargetMatrixHost host = new();
        host._thread.Start();
        await host._ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        return host;
    }

    internal async ValueTask FocusAsync(WpfMatrixTarget target)
    {
        System.Windows.Window window = _window ??
            throw new InvalidOperationException("The controlled WPF target is not ready.");
        await window.Dispatcher.InvokeAsync(() =>
        {
            System.Windows.UIElement element = Resolve(target);
            window.Show();
            window.WindowState = System.Windows.WindowState.Normal;
            _ = window.Activate();
            _ = TestNativeMethods.SetForegroundWindow(
                new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = element.Focus();
            _ = System.Windows.Input.Keyboard.Focus(element);
            if (element is System.Windows.Controls.TextBox textBox)
            {
                textBox.CaretIndex = textBox.Text.Length;
                textBox.SelectionLength = 0;
            }
        });
        await Task.Delay(125);
    }

    internal async ValueTask SelectAllTextBoxAsync()
    {
        System.Windows.Window window = _window ??
            throw new InvalidOperationException("The controlled WPF target is not ready.");
        await window.Dispatcher.InvokeAsync(() =>
        {
            System.Windows.Controls.TextBox textBox = _textBox ??
                throw new InvalidOperationException("The controlled WPF target is not ready.");
            _ = window.Activate();
            _ = TestNativeMethods.SetForegroundWindow(
                new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = textBox.Focus();
            textBox.SelectAll();
        });
        await Task.Delay(100);
    }

    internal async ValueTask WaitForTextAsync(
        WpfMatrixTarget target,
        int expectedLength)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(3))
        {
            int length = await GetTextLengthAsync(target);
            if (length == expectedLength)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            "The controlled WPF target did not receive the expected insertion.");
    }

    public async ValueTask DisposeAsync()
    {
        System.Windows.Window? window = _window;
        if (window is not null)
        {
            try
            {
                await window.Dispatcher.InvokeAsync(window.Close);
            }
            catch (TaskCanceledException)
            {
                // The controlled window dispatcher already exited.
            }
        }

        _ = _thread.Join(millisecondsTimeout: 750);
    }

    private async ValueTask<int> GetTextLengthAsync(WpfMatrixTarget target)
    {
        System.Windows.Window window = _window ??
            throw new InvalidOperationException("The controlled WPF target is not ready.");
        return await window.Dispatcher.InvokeAsync(() =>
        {
            if (target == WpfMatrixTarget.RichTextBox)
            {
                System.Windows.Controls.RichTextBox richTextBox = _richTextBox ??
                    throw new InvalidOperationException("The controlled WPF target is not ready.");
                return new System.Windows.Documents.TextRange(
                    richTextBox.Document.ContentStart,
                    richTextBox.Document.ContentEnd).Text.TrimEnd('\r', '\n').Length;
            }

            return ((System.Windows.Controls.TextBox)Resolve(target)).Text.Length;
        });
    }

    private System.Windows.UIElement Resolve(WpfMatrixTarget target)
    {
        System.Windows.UIElement? element = target switch
        {
            WpfMatrixTarget.TextBox => _textBox,
            WpfMatrixTarget.RichTextBox => _richTextBox,
            WpfMatrixTarget.PasswordBox => _passwordBox,
            WpfMatrixTarget.ReadOnlyTextBox => _readOnlyTextBox,
            WpfMatrixTarget.SecondTextBox => _secondTextBox,
            _ => null
        };
        return element ??
            throw new InvalidOperationException("The controlled WPF target is not ready.");
    }

    private void ThreadMain()
    {
        System.Windows.Window window = new()
        {
            Title = "Soltex Whisper target matrix",
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = 24,
            Top = 24,
            Width = 460,
            Height = 360,
            ResizeMode = System.Windows.ResizeMode.NoResize
        };
        System.Windows.Controls.StackPanel panel = new()
        {
            Margin = new System.Windows.Thickness(16)
        };
        _textBox = new System.Windows.Controls.TextBox
        {
            Text = "plain fixture",
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        };
        _richTextBox = new System.Windows.Controls.RichTextBox
        {
            Height = 70,
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        };
        _passwordBox = new System.Windows.Controls.PasswordBox
        {
            Password = "protected fixture",
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        };
        _readOnlyTextBox = new System.Windows.Controls.TextBox
        {
            Text = "read only fixture",
            IsReadOnly = true,
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        };
        _secondTextBox = new System.Windows.Controls.TextBox
        {
            Text = "focus drift fixture"
        };
        _ = panel.Children.Add(_textBox);
        _ = panel.Children.Add(_richTextBox);
        _ = panel.Children.Add(_passwordBox);
        _ = panel.Children.Add(_readOnlyTextBox);
        _ = panel.Children.Add(_secondTextBox);
        window.Content = panel;
        window.Closed += (_, _) =>
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                System.Windows.Threading.DispatcherPriority.Normal);
        _window = window;
        window.Show();
        _ready.TrySetResult();
        System.Windows.Threading.Dispatcher.Run();
    }
}

internal sealed class LiveTextTarget : IAsyncDisposable
{
    private readonly string _initialText;
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private System.Windows.Forms.Form? _form;
    private System.Windows.Forms.TextBox? _textBox;
    private int _enterCount;

    private LiveTextTarget(string initialText)
    {
        _initialText = initialText;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Soltex Whisper controlled target"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal static async ValueTask<LiveTextTarget> CreateAsync(string initialText)
    {
        LiveTextTarget target = new(initialText);
        target._thread.Start();
        await target._ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        return target;
    }

    internal async ValueTask FocusAsync()
    {
        System.Windows.Forms.Form form = _form ??
            throw new InvalidOperationException("The controlled target is not ready.");
        System.Windows.Forms.TextBox textBox = _textBox ??
            throw new InvalidOperationException("The controlled target is not ready.");
        TaskCompletionSource focused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = form.BeginInvoke(() =>
        {
            form.Show();
            form.WindowState = System.Windows.Forms.FormWindowState.Normal;
            form.BringToFront();
            form.Activate();
            textBox.Focus();
            textBox.SelectionStart = textBox.TextLength;
            textBox.SelectionLength = 0;
            _ = TestNativeMethods.SetForegroundWindow(form.Handle);
            focused.TrySetResult();
        });
        await focused.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);
    }

    internal async ValueTask<string> GetTextAsync()
    {
        System.Windows.Forms.TextBox textBox = _textBox ??
            throw new InvalidOperationException("The controlled target is not ready.");
        TaskCompletionSource<string> value = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = textBox.BeginInvoke(() => value.TrySetResult(textBox.Text));
        return await value.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    internal async ValueTask SelectAllAsync()
    {
        System.Windows.Forms.Form form = _form ??
            throw new InvalidOperationException("The controlled target is not ready.");
        System.Windows.Forms.TextBox textBox = _textBox ??
            throw new InvalidOperationException("The controlled target is not ready.");
        TaskCompletionSource selected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = textBox.BeginInvoke(() =>
        {
            form.BringToFront();
            form.Activate();
            textBox.Focus();
            textBox.SelectAll();
            _ = TestNativeMethods.SetForegroundWindow(form.Handle);
            selected.TrySetResult();
        });
        await selected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);
    }

    internal async ValueTask WaitForTextAsync(int expectedLength)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            if ((await GetTextAsync()).Length == expectedLength)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The controlled text target did not receive the insertion.");
    }

    internal int EnterCount => Volatile.Read(ref _enterCount);

    internal async ValueTask WaitForEnterAsync(int expectedCount)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (EnterCount == expectedCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            "The controlled text target did not receive exactly one Enter event.");
    }

    public async ValueTask DisposeAsync()
    {
        System.Windows.Forms.Form? form = _form;
        if (form is not null && !form.IsDisposed)
        {
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _ = form.BeginInvoke(() =>
                {
                    form.FormClosed += (_, _) => closed.TrySetResult();
                    form.Close();
                });
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (InvalidOperationException)
            {
                // The controlled window already exited.
            }
        }

        _ = _thread.Join(millisecondsTimeout: 500);
    }

    private void ThreadMain()
    {
        using System.Windows.Forms.Form form = new()
        {
            Text = "Soltex insertion target",
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(24, 24),
            ClientSize = new System.Drawing.Size(420, 72),
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        };
        using System.Windows.Forms.TextBox textBox = new()
        {
            Text = _initialText,
            Location = new System.Drawing.Point(12, 18),
            Width = 390
        };
        textBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == System.Windows.Forms.Keys.Enter)
            {
                _ = Interlocked.Increment(ref _enterCount);
            }
        };
        form.Controls.Add(textBox);
        _form = form;
        _textBox = textBox;
        form.Shown += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectionStart = textBox.TextLength;
            _ready.TrySetResult();
        };
        System.Windows.Forms.Application.Run(form);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    internal uint Type;
    internal NativeInputUnion Data;

    internal static NativeInput Keyboard(ushort virtualKey, bool keyUp) => new()
    {
        Type = 1,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? 0x0002u : 0u
            }
        }
    };
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    internal NativeKeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    internal ushort VirtualKey;
    internal ushort ScanCode;
    internal uint Flags;
    internal uint Time;
    internal nuint ExtraInfo;
}

internal static class TestNativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);
}
