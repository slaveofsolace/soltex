using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Soltex.Whisper;
using Soltex.Whisper.Windows;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

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
    ("provider credentials stay in the protected Windows boundary", ProviderCredentialBoundary)
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
        (TargetObservation("code", WindowsWhisperControlKind.Document, textPattern: true),
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
    bool password = false) => new(
        ProcessId: 2048,
        processName,
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
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
