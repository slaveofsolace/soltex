using System.Diagnostics;
using Soltex.Whisper;

var tests = new (string Name, Action Run)[]
{
    ("terminal submit phrase is stripped only at the end", () =>
    {
        WhisperPipelineResult result = Pipe("Send it now. press enter.");
        Equal("Send it now.", result.Text);
        True(result.SubmitRequested);
        Equal(WhisperSubmitOrigin.TerminalPhrase, result.SubmitOrigin);
    }),
    ("middle submit phrase remains ordinary text", () =>
    {
        WhisperPipelineResult result = Pipe("Do not press enter after review");
        Equal("Do not press enter after review", result.Text);
        False(result.SubmitRequested);
    }),
    ("submit-only transcript is supported", () =>
    {
        WhisperPipelineResult result = Pipe("press enter!");
        Equal(string.Empty, result.Text);
        True(result.SubmitRequested);
    }),
    ("exact snippet expansion preserves content", () =>
    {
        const string content = "First line\nSecond line, exactly.";
        WhisperPipelineResult result = new WhisperTextPipeline().Process(
            "insert response",
            [new WhisperSnippet("insert response", content)]);
        Equal(content, result.Text);
        True(result.AppliedOperations.Contains("snippet-expansion", StringComparer.Ordinal));
        False(result.AppliedOperations.Contains("smart-formatting", StringComparer.Ordinal));
    }),
    ("duplicate snippet cues are rejected", () =>
        Throws<ArgumentException>(() => new WhisperTextPipeline().Process(
            "signature",
            [new WhisperSnippet("signature", "A"), new WhisperSnippet("SIGNATURE", "B")]))),
    ("spoken punctuation and paragraphs are deterministic", () =>
        Equal("hello, world.\n\nnext line", Pipe("hello comma world period new paragraph next line").Text)),
    ("full stop is accepted as two words", () =>
        Equal("hello. next", Pipe("hello full stop next").Text)),
    ("backtrack removes the preceding token", () =>
        Equal("meet Wednesday", Pipe("meet Tuesday scratch that Wednesday").Text)),
    ("oversized transcripts are rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => Pipe(
            new string('a', WhisperLimits.MaximumTranscriptCharacters + 1)))),
    ("shortcut chords enforce one to three unique keys", () =>
    {
        Equal("Ctrl+Win+Alt", WhisperShortcutBinding.Create(
            WhisperShortcutAction.CommandMode,
            "control",
            "windows",
            "alt").DisplayText);
        Throws<ArgumentException>(() => WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Ctrl",
            "Ctrl"));
        Throws<ArgumentException>(() => WhisperShortcutBinding.Create(
            WhisperShortcutAction.PushToTalk,
            "Ctrl",
            "Win",
            "Alt",
            "Space"));
    }),
    ("shortcut sets allow four bindings per action", () =>
    {
        WhisperShortcutSet set = new(
        [
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Ctrl", "Win"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Alt", "Space"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Mouse 4"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Mouse 5")
        ]);
        Equal(4, set.ForAction(WhisperShortcutAction.PushToTalk).Count);
    }),
    ("shortcut conflicts across actions are rejected", () =>
        Throws<ArgumentException>(() => new WhisperShortcutSet(
        [
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Ctrl", "Win"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.HandsFree, "Ctrl", "Win")
        ]))),
    ("shortcut conflicts ignore key order", () =>
        Throws<ArgumentException>(() => new WhisperShortcutSet(
        [
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Ctrl", "Win", "Space"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.CommandMode, "Space", "Win", "Ctrl")
        ]))),
    ("default shortcuts expose the public parity scaffold", () =>
    {
        WhisperShortcutSet set = WhisperShortcutSet.CreateDefault();
        Equal(7, set.Bindings.Count);
        Equal("Ctrl+Win", set.ForAction(WhisperShortcutAction.PushToTalk)[0].DisplayText);
        Equal("Ctrl+Win+Space", set.ForAction(WhisperShortcutAction.HandsFree)[0].DisplayText);
        Equal("Ctrl+Win+Alt", set.ForAction(WhisperShortcutAction.CommandMode)[0].DisplayText);
    }),
    ("auto-send remains disabled globally by default", () =>
    {
        WhisperDeliveryDecision decision = Policy(Pipe("hello press enter"), Edit("chat"),
            new WhisperAppProfile("chat", autoSendAllowed: true), autoSend: false);
        Equal(WhisperDeliveryKind.InsertText, decision.Kind);
    }),
    ("auto-send requires an exact per-application profile", () =>
    {
        WhisperDeliveryDecision decision = Policy(Pipe("hello press enter"), Edit("chat"),
            new WhisperAppProfile("mail", autoSendAllowed: true), autoSend: true);
        Equal(WhisperDeliveryKind.InsertText, decision.Kind);
    }),
    ("password targets fail closed", () =>
    {
        WhisperTargetContext password = new("browser", WhisperTargetKind.PlainText, true, true, true, false, false);
        Equal(WhisperDeliveryKind.None, Policy(Pipe("secret press enter"), password,
            new WhisperAppProfile("browser", true), true).Kind);
    }),
    ("unknown targets fall back to copy", () =>
        Equal(WhisperDeliveryKind.CopyText, Policy(Pipe("recoverable text"),
            WhisperTargetContext.Unknown, null, false).Kind)),
    ("elevated targets fall back to copy from standard Soltex", () =>
        Equal(WhisperDeliveryKind.CopyText, Policy(Pipe("recoverable text"),
            Edit("admin-editor", elevated: true), new WhisperAppProfile("admin-editor", false), false).Kind)),
    ("terminal submission requires a second opt-in", () =>
    {
        WhisperTargetContext terminal = Edit("pwsh", WhisperTargetKind.Terminal);
        WhisperPipelineResult pipeline = Pipe("Get-Date press enter");
        Equal(WhisperDeliveryKind.InsertText, Policy(pipeline, terminal,
            new WhisperAppProfile("pwsh", true, terminalAutoSendAllowed: false), true).Kind);
        Equal(WhisperDeliveryKind.InsertAndSubmit, Policy(pipeline, terminal,
            new WhisperAppProfile("pwsh", true, terminalAutoSendAllowed: true), true).Kind);
    }),
    ("dedicated submit shortcut has a distinct origin", () =>
    {
        WhisperDeliveryDecision decision = new WhisperDeliveryPolicy().Evaluate(
            Pipe("hello"), Edit("chat"), new WhisperAppProfile("chat", true),
            autoSendEnabled: true, dedicatedSubmitShortcut: true);
        Equal(WhisperDeliveryKind.InsertAndSubmit, decision.Kind);
        Equal(WhisperSubmitOrigin.DedicatedShortcut, decision.SubmitOrigin);
    }),
    ("submit-only delivery is supported after authorization", () =>
        Equal(WhisperDeliveryKind.SubmitOnly, Policy(Pipe("press enter"), Edit("chat"),
            new WhisperAppProfile("chat", true), true).Kind)),
    ("history is memory-bounded", () =>
    {
        BoundedWhisperHistory history = new(2);
        history.Add(Entry("one", "first"));
        history.Add(Entry("two", "second"));
        IReadOnlyList<WhisperHistoryEntry> snapshot = history.Add(Entry("three", "third"));
        Equal(2, snapshot.Count);
        Equal("second", snapshot[0].Text);
        Equal("third", snapshot[1].Text);
    }),
    ("coordinator records finalized delivery when history is supplied", () =>
    {
        BoundedWhisperHistory history = new(4);
        WhisperCoordinator coordinator = new(history: history);
        WhisperFinalizationResult result = coordinator.Finalize(new WhisperFinalizationRequest(
            "hello press enter",
            Edit("chat"),
            new WhisperAppProfile("chat", true),
            Array.Empty<WhisperSnippet>(),
            WhisperTextOptions.Default,
            AutoSendEnabled: true,
            DedicatedSubmitShortcut: false,
            SoltexIsElevated: false), DateTimeOffset.UtcNow);
        Equal(WhisperDeliveryKind.InsertAndSubmit, result.Delivery.Kind);
        Equal(1, history.CreateSnapshot().Count);
    }),
    ("session lifecycle is explicit", () =>
    {
        WhisperSessionController session = new();
        session.BeginListening(WhisperCaptureMode.PushToTalk, DateTimeOffset.UtcNow);
        session.MarkTranscribing();
        session.MarkProcessing();
        session.MarkDelivering();
        session.Complete();
        Equal(WhisperSessionState.Completed, session.CreateSnapshot().State);
        session.Reset();
        Equal(WhisperSessionState.Idle, session.CreateSnapshot().State);
    }),
    ("invalid session transitions are rejected", () =>
        Throws<InvalidOperationException>(new WhisperSessionController().MarkProcessing)),
    ("hands-free warning and expiry use nineteen and twenty minutes", () =>
    {
        WhisperSessionController session = new();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        session.BeginListening(WhisperCaptureMode.HandsFree, started);
        Equal(WhisperDurationState.Current, session.GetHandsFreeDurationState(started.AddMinutes(18).AddSeconds(59)));
        Equal(WhisperDurationState.Warning, session.GetHandsFreeDurationState(started.AddMinutes(19)));
        Equal(WhisperDurationState.Expired, session.GetHandsFreeDurationState(started.AddMinutes(20)));
    }),
    ("audio clips enforce format and duration bounds", () =>
    {
        WhisperAudioClip clip = new(new byte[] { 0, 0, 1, 0 }, 16_000, 1, TimeSpan.FromMilliseconds(1));
        Equal(16_000, clip.SampleRateHz);
        Throws<ArgumentOutOfRangeException>(() => new WhisperAudioClip(
            new byte[] { 0, 0 }, 4_000, 1, TimeSpan.FromSeconds(1)));
        Throws<ArgumentOutOfRangeException>(() => new WhisperAudioClip(
            new byte[] { 0, 0 }, 16_000, 1, TimeSpan.FromMinutes(21)));
    })
};

int failed = 0;
Stopwatch suite = Stopwatch.StartNew();
foreach ((string name, Action run) in tests)
{
    Stopwatch test = Stopwatch.StartNew();
    try
    {
        run();
        test.Stop();
        Console.WriteLine($"PASS {name} ({test.Elapsed.TotalMilliseconds:F1} ms)");
    }
    catch (Exception exception)
    {
        test.Stop();
        failed++;
        Console.WriteLine($"FAIL {name} ({test.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine("     " + exception.Message);
    }
}

suite.Stop();
Console.WriteLine();
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
Console.WriteLine($"MEASURE whisper_suite tests={tests.Length} failed={failed} total_ms={suite.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static WhisperPipelineResult Pipe(string text) => new WhisperTextPipeline().Process(text);

static WhisperDeliveryDecision Policy(
    WhisperPipelineResult pipeline,
    WhisperTargetContext target,
    WhisperAppProfile? profile,
    bool autoSend) => new WhisperDeliveryPolicy().Evaluate(pipeline, target, profile, autoSend);

static WhisperTargetContext Edit(
    string processName,
    WhisperTargetKind kind = WhisperTargetKind.PlainText,
    bool elevated = false) => new(
        processName,
        kind,
        isKnown: true,
        isEditable: true,
        isPassword: false,
        isReadOnly: false,
        isElevated: elevated);

static WhisperHistoryEntry Entry(string processName, string text) => new(
    DateTimeOffset.UtcNow,
    processName,
    WhisperDeliveryKind.InsertText,
    text);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool condition) => True(!condition);

static void Throws<TException>(Action action) where TException : Exception
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
