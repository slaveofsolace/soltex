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
        Equal("Ctrl+Alt+Space", set.ForAction(WhisperShortcutAction.PushToTalk)[0].DisplayText);
        Equal("Ctrl+Alt+H", set.ForAction(WhisperShortcutAction.HandsFree)[0].DisplayText);
        Equal("Ctrl+Alt+C", set.ForAction(WhisperShortcutAction.CommandMode)[0].DisplayText);
        True(WhisperShortcutRegistrationPolicy.Validate(set).IsValid);
    }),
    ("shortcut registration rejects operating-system and broad chords with reasons", () =>
    {
        WhisperShortcutValidationResult windowsKey = WhisperShortcutRegistrationPolicy.Validate(
            new WhisperShortcutSet(
            [
                WhisperShortcutBinding.Create(
                    WhisperShortcutAction.OpenScratchpad,
                    "Win",
                    "S")
            ]));
        False(windowsKey.IsValid);
        True(windowsKey.Error?.Contains("reserved", StringComparison.OrdinalIgnoreCase) == true);

        WhisperShortcutValidationResult broad = WhisperShortcutRegistrationPolicy.Validate(
            new WhisperShortcutSet(
            [
                WhisperShortcutBinding.Create(
                    WhisperShortcutAction.PushToTalk,
                    "Ctrl",
                    "Alt")
            ]));
        False(broad.IsValid);
        True(broad.Error?.Contains("too broad", StringComparison.OrdinalIgnoreCase) == true);
    }),
    ("shortcut registration rejects ambiguous prefix combinations", () =>
    {
        WhisperShortcutValidationResult result = WhisperShortcutRegistrationPolicy.Validate(
            new WhisperShortcutSet(
            [
                WhisperShortcutBinding.Create(
                    WhisperShortcutAction.PushToTalk,
                    "Ctrl",
                    "Space"),
                WhisperShortcutBinding.Create(
                    WhisperShortcutAction.HandsFree,
                    "Ctrl",
                    "Alt",
                    "Space")
            ]));
        False(result.IsValid);
        True(result.Error?.Contains("overlap", StringComparison.OrdinalIgnoreCase) == true);
    }),
    ("shortcut gestures preserve hold release and double-tap locking", () =>
    {
        WhisperShortcutGestureInterpreter gestures = new();
        Equal<WhisperShortcutIntent?>(
            WhisperShortcutIntent.BeginPushToTalk,
            gestures.Observe(new WhisperShortcutSignal(
                WhisperShortcutAction.PushToTalk,
                WhisperShortcutTransition.Pressed,
                TimeSpan.FromSeconds(1))));
        Equal<WhisperShortcutIntent?>(
            WhisperShortcutIntent.EndPushToTalk,
            gestures.Observe(new WhisperShortcutSignal(
                WhisperShortcutAction.PushToTalk,
                WhisperShortcutTransition.Released,
                TimeSpan.FromSeconds(2))));
        Equal<WhisperShortcutIntent?>(
            WhisperShortcutIntent.ToggleHandsFree,
            gestures.Observe(new WhisperShortcutSignal(
                WhisperShortcutAction.HandsFree,
                WhisperShortcutTransition.Pressed,
                TimeSpan.FromSeconds(3))));
        Equal<WhisperShortcutIntent?>(null, gestures.Observe(new WhisperShortcutSignal(
            WhisperShortcutAction.HandsFree,
            WhisperShortcutTransition.Released,
            TimeSpan.FromSeconds(3.1))));
        Equal<WhisperShortcutIntent?>(
            WhisperShortcutIntent.LockHandsFree,
            gestures.Observe(new WhisperShortcutSignal(
                WhisperShortcutAction.HandsFree,
                WhisperShortcutTransition.Pressed,
                TimeSpan.FromSeconds(3.3))));
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
        using WhisperAudioClip clip = new(new byte[] { 0, 0, 1, 0 }, 16_000, 1, TimeSpan.FromMilliseconds(1));
        Equal(16_000, clip.SampleRateHz);
        Throws<ArgumentOutOfRangeException>(() => new WhisperAudioClip(
            new byte[] { 0, 0 }, 4_000, 1, TimeSpan.FromSeconds(1)));
        Throws<ArgumentOutOfRangeException>(() => new WhisperAudioClip(
            new byte[] { 0, 0 }, 16_000, 1, TimeSpan.FromMinutes(21)));
    }),
    ("owned audio clips clear their exact buffer on disposal", () =>
    {
        byte[] owned = [1, 2, 3, 4];
        WhisperAudioClip clip = WhisperAudioClip.CreateOwned(
            owned,
            16_000,
            1,
            TimeSpan.FromMilliseconds(1));
        clip.Dispose();
        True(owned.All(value => value == 0));
        Throws<ObjectDisposedException>(() => _ = clip.Pcm16);
    }),

    // ---------- Insertion authorization ----------

    ("insertion authorization accepts the unchanged captured target", () =>
    {
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            InsertRequest(Snap("chat", "el-1")),
            Snap("chat", "el-1"));
        Equal(WhisperInsertionAction.Insert, authorization.Action);
        Equal(WhisperInsertionFallbackReason.None, authorization.FallbackReason);
    }),
    ("insertion authorization copies when the target is unknown", () =>
    {
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            InsertRequest(Snap("chat", "el-1")),
            currentTarget: null);
        Equal(WhisperInsertionAction.Copy, authorization.Action);
        Equal(WhisperInsertionFallbackReason.TargetUnknown, authorization.FallbackReason);
    }),
    ("insertion authorization copies when focus changes", () =>
    {
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            InsertRequest(Snap("chat", "el-1")),
            Snap("mail", "el-2", processId: 42));
        Equal(WhisperInsertionAction.Copy, authorization.Action);
        Equal(WhisperInsertionFallbackReason.TargetChanged, authorization.FallbackReason);
    }),
    ("insertion authorization copies when a protected field appears", () =>
    {
        WhisperTargetSnapshot protectedTarget = new(
            new WhisperTargetIdentity(7, "chat", "el-1"),
            new WhisperTargetContext(
                "chat",
                WhisperTargetKind.PlainText,
                isKnown: true,
                isEditable: false,
                isPassword: true,
                isReadOnly: true,
                isElevated: false));
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            InsertRequest(Snap("chat", "el-1")),
            protectedTarget);
        Equal(WhisperInsertionAction.Copy, authorization.Action);
        Equal(WhisperInsertionFallbackReason.ProtectedField, authorization.FallbackReason);
    }),
    ("a policy copy decision cannot be upgraded to insertion", () =>
    {
        WhisperDeliveryDecision copy = new(
            WhisperDeliveryKind.CopyText,
            "hello",
            WhisperSubmitOrigin.None,
            RestoreClipboard: false,
            "safe fallback");
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            new WhisperTextDeliveryRequest(copy, CapturedTarget: null),
            Snap("chat", "el-1"));
        Equal(WhisperInsertionAction.Copy, authorization.Action);
        Equal(WhisperInsertionFallbackReason.PolicyRequiredCopy, authorization.FallbackReason);
    }),
    ("submit-only decisions do not create an insertion action", () =>
    {
        WhisperDeliveryDecision submitOnly = new(
            WhisperDeliveryKind.SubmitOnly,
            string.Empty,
            WhisperSubmitOrigin.DedicatedShortcut,
            RestoreClipboard: false,
            "authorized");
        Equal(
            WhisperInsertionAction.None,
            WhisperInsertionPolicy.Evaluate(
                new WhisperTextDeliveryRequest(submitOnly, Snap("chat", "el-1")),
                Snap("chat", "el-1")).Action);
    }),

    // ---------- Target identity and submit gate ----------

    ("an unchanged target reports no drift", () =>
        Equal(WhisperTargetDrift.None, Snap("chat", "el-1").CompareWith(Snap("chat", "el-1")))),
    ("a different focused element is drift", () =>
        Equal(WhisperTargetDrift.ElementChanged, Snap("chat", "el-1").CompareWith(Snap("chat", "el-2")))),
    ("a different process is drift", () =>
        Equal(WhisperTargetDrift.ProcessChanged, Snap("chat", "el-1").CompareWith(Snap("mail", "el-1", processId: 42)))),
    ("an unknown current target is drift, never a match", () =>
    {
        WhisperTargetSnapshot unknown = new(
            new WhisperTargetIdentity(7, "chat", "el-1"),
            new WhisperTargetContext("chat", WhisperTargetKind.PlainText, false, false, false, false, false));
        Equal(WhisperTargetDrift.TargetUnknown, Snap("chat", "el-1").CompareWith(unknown));
    }),
    ("a field that became a password is drift", () =>
    {
        WhisperTargetSnapshot password = new(
            new WhisperTargetIdentity(7, "chat", "el-1"),
            new WhisperTargetContext("chat", WhisperTargetKind.PlainText, true, true, true, false, false));
        Equal(WhisperTargetDrift.ProtectedFieldAppeared, Snap("chat", "el-1").CompareWith(password));
    }),
    ("identity requires the process to agree with the context", () =>
        Throws<ArgumentException>(() => new WhisperTargetSnapshot(
            new WhisperTargetIdentity(7, "chat", "el-1"),
            Edit("mail")))),

    ("insertion verification accepts an exact match", () =>
        True(WhisperInsertionVerifier.Verify(
            "hello there", "hello there",
            WhisperVerificationMethod.AutomationValueRead,
            WhisperTargetKind.PlainText).Verified)),
    ("insertion verification accepts a field ending with the transcript", () =>
        True(WhisperInsertionVerifier.Verify(
            "world", "hello world",
            WhisperVerificationMethod.AutomationValueRead,
            WhisperTargetKind.PlainText).Verified)),
    ("insertion verification tolerates documented rich-text normalization", () =>
        True(WhisperInsertionVerifier.Verify(
            "hello world", "hello\u00A0world\u200B\r\n",
            WhisperVerificationMethod.AutomationTextRead,
            WhisperTargetKind.RichText).Verified)),
    ("insertion verification rejects altered text", () =>
        False(WhisperInsertionVerifier.Verify(
            "transfer 100", "transfer 1000",
            WhisperVerificationMethod.AutomationValueRead,
            WhisperTargetKind.PlainText).Verified)),
    ("insertion verification fails closed when no read is possible", () =>
    {
        WhisperInsertionVerification verification = WhisperInsertionVerifier.Verify(
            "hello", "hello", WhisperVerificationMethod.None, WhisperTargetKind.PlainText);
        False(verification.Verified);
        Equal(WhisperVerificationMethod.None, verification.Method);
    }),

    ("the submit gate allows a verified, unchanged target", () =>
    {
        WhisperSubmitAuthorization authorization = Gate(
            Snap("chat", "el-1"), Verified());
        True(authorization.Allowed);
        Equal(WhisperTargetDrift.None, authorization.Drift);
        True(authorization.TryConsume());
        False(authorization.TryConsume());
    }),
    ("the submit gate denies unverified insertion", () =>
        False(Gate(Snap("chat", "el-1"), WhisperInsertionVerification.Unavailable).Allowed)),
    ("the submit gate denies a target that changed after insertion", () =>
    {
        WhisperSubmitAuthorization authorization = Gate(Snap("chat", "el-2"), Verified());
        False(authorization.Allowed);
        Equal(WhisperTargetDrift.ElementChanged, authorization.Drift);
    }),
    ("the submit gate denies until the first-use warning is accepted", () =>
        False(Gate(Snap("chat", "el-1"), Verified(), warningAccepted: false).Allowed)),
    ("the submit gate denies after cancellation", () =>
    {
        WhisperSubmitAuthorization authorization =
            Gate(Snap("chat", "el-1"), Verified(), cancelled: true);
        False(authorization.Allowed);
        False(authorization.TryConsume());
    }),
    ("the submit gate denies an unavailable current target", () =>
    {
        WhisperSubmitAuthorization authorization = Gate(null, Verified());
        False(authorization.Allowed);
        Equal(WhisperTargetDrift.TargetUnknown, authorization.Drift);
    }),
    ("the submit gate ignores decisions that never requested submission", () =>
    {
        WhisperDeliveryDecision insertOnly = new(
            WhisperDeliveryKind.InsertText, "hello", WhisperSubmitOrigin.None, false, "insert");
        False(WhisperSubmitGate.Evaluate(
            insertOnly, Snap("chat", "el-1"), Snap("chat", "el-1"), Verified(), true, false).Allowed);
    }),

    // ---------- Vocabulary, styles, languages ----------

    ("dictionary terms are normalized and deduplicated", () =>
    {
        WhisperVocabulary dictionary = new(["  Soltex ", "soltex", "Whisper\tflow"]);
        Equal(2, dictionary.Terms.Count);
        Equal("Soltex", dictionary.Terms[0]);
        Equal("Whisper flow", dictionary.Terms[1]);
        True(dictionary.Contains("SOLTEX"));
    }),
    ("oversized dictionary terms are rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => new WhisperVocabulary(
            [new string('a', WhisperVocabulary.MaximumTermCharacters + 1)]))),
    ("a terminal target always resolves to the terminal style", () =>
    {
        WhisperStyleProfile style = WhisperStyleProfile.Resolve(
            Edit("pwsh", WhisperTargetKind.Terminal),
            new WhisperAppProfile("pwsh", false, styleName: "Email"));
        Equal(WhisperStyleKind.Terminal, style.Kind);
        False(style.ProseCleanup);
    }),
    ("an application profile selects its named style", () =>
        Equal(WhisperStyleKind.Email, WhisperStyleProfile.Resolve(
            Edit("outlook"), new WhisperAppProfile("outlook", false, styleName: "Email")).Kind)),
    ("the terminal style keeps the submit phrase but drops prose cleanup", () =>
    {
        WhisperTextOptions options = WhisperStyleProfile.Terminal.ToTextOptions();
        True(options.DetectTerminalSubmit);
        False(options.SmartFormatting);
        False(options.Backtrack);
    }),
    ("explicit languages must be real culture names", () =>
    {
        Equal("fr", WhisperLanguageSelection.Explicit("fr").LanguageTag);
        True(WhisperLanguageSelection.AutoDetect.IsAutoDetect);
        Throws<ArgumentException>(() => WhisperLanguageSelection.Explicit("not-a-language"));
    }),

    // ---------- Command Mode ----------

    ("command mode maps known phrases", () =>
    {
        Equal(WhisperCommandKind.Shorten, WhisperCommandParser.Parse("make it shorter", true).Kind);
        Equal(WhisperCommandKind.FixGrammar, WhisperCommandParser.Parse("Fix grammar.", true).Kind);
        Equal(WhisperCommandKind.ConvertToBullets, WhisperCommandParser.Parse("convert to bullets", true).Kind);
    }),
    ("command mode refuses instructions it does not know", () =>
    {
        WhisperCommandRequest request = WhisperCommandParser.Parse("delete the production database", true);
        False(request.IsSupported);
        Equal(WhisperCommandKind.Unsupported, request.Kind);
    }),
    ("command mode translates only into offered languages", () =>
    {
        Equal("ja", WhisperCommandParser.Parse("translate to Japanese", true).TargetLanguageTag);
        False(WhisperCommandParser.Parse("translate to Klingon", true).IsSupported);
    }),
    ("meaning-changing transforms preview before replacing a selection", () =>
    {
        True(WhisperCommandParser.Parse("summarize", true).RequiresPreview);
        False(WhisperCommandParser.Parse("summarize", false).RequiresPreview);
        False(WhisperCommandParser.Parse("fix grammar", true).RequiresPreview);
    }),
    ("command mode uses the caret when nothing is selected", () =>
        Equal(WhisperCommandScope.Caret, WhisperCommandParser.Parse("rewrite", false).Scope)),

    // ---------- Settings validation and migration ----------

    ("default settings keep every outward capability off", () =>
    {
        WhisperSettings defaults = WhisperSettings.CreateDefault();
        False(defaults.AutoSendEnabled);
        False(defaults.ContextReadsAllowed);
        False(defaults.ShowTranscriptPreview);
        Equal(WhisperHistoryMode.SessionMemory, defaults.HistoryMode);
    }),
    ("a missing settings document falls back to defaults", () =>
    {
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(null);
        False(result.Settings.AutoSendEnabled);
        Equal(1, result.Corrections.Count);
    }),
    ("auto-send without a recorded warning is disabled on load", () =>
    {
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(new WhisperSettingsDocument
        {
            Version = WhisperSettings.CurrentVersion,
            AutoSendEnabled = true,
            AutoSendWarningAccepted = false
        });
        False(result.Settings.AutoSendEnabled);
        True(result.Corrections.Count > 0);
    }),
    ("migrating from an older version resets auto-send consent", () =>
    {
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(new WhisperSettingsDocument
        {
            Version = 1,
            AutoSendEnabled = true,
            AutoSendWarningAccepted = true
        });
        True(result.Migrated);
        False(result.Settings.AutoSendEnabled);
        False(result.Settings.AutoSendWarningAccepted);
    }),
    ("an unsupported future version falls back to defaults", () =>
    {
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(new WhisperSettingsDocument
        {
            Version = WhisperSettings.CurrentVersion + 1,
            AutoSendEnabled = true
        });
        False(result.Settings.AutoSendEnabled);
        False(result.Migrated);
    }),
    ("invalid fields are repaired to the safer default and reported", () =>
    {
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(new WhisperSettingsDocument
        {
            Version = WhisperSettings.CurrentVersion,
            PreferredLanguageTag = "zzz-not-real",
            HistoryMode = "EverythingForever",
            HistoryRetentionDays = 5_000,
            ClipboardBehavior = "KeepForever",
            VocabularyTerms = ["ok", "  ", "also fine"]
        });
        True(result.Settings.Language.IsAutoDetect);
        Equal(WhisperHistoryMode.SessionMemory, result.Settings.HistoryMode);
        Equal(7, result.Settings.HistoryRetentionDays);
        Equal(WhisperClipboardBehavior.RestorePrevious, result.Settings.ClipboardBehavior);
        Equal(2, result.Settings.Vocabulary.Terms.Count);
        Equal(5, result.Corrections.Count);
    }),
    ("a clean current document round-trips without corrections", () =>
    {
        WhisperSettings source = WhisperSettings.CreateDefault();
        WhisperSettingsLoadResult result = WhisperSettingsMigrator.Load(source.ToDocument());
        True(result.IsClean);
        Equal(WhisperSettings.CurrentVersion, result.LoadedVersion);
    }),

    // ---------- Readiness ----------

    ("a fully configured Whisper reports ready", () =>
    {
        WhisperReadinessReport report = WhisperReadinessEvaluator.Evaluate(ReadyInputs());
        True(report.CanDictate);
        True(report.PrimaryBlocker is null);
    }),
    ("a missing microphone permission blocks dictation with one action", () =>
    {
        WhisperReadinessReport report = WhisperReadinessEvaluator.Evaluate(
            ReadyInputs() with { MicrophonePermissionGranted = false });
        False(report.CanDictate);
        Equal("microphone", report.PrimaryBlocker!.Id);
        Equal(WhisperReadinessState.Blocked, report.PrimaryBlocker.State);
        True(report.PrimaryBlocker.NextAction is { Length: > 0 });
    }),
    ("a microphone runtime failure blocks readiness without exposing raw detail", () =>
    {
        WhisperReadinessReport report = WhisperReadinessEvaluator.Evaluate(
            ReadyInputs() with { MicrophoneError = @"busy at C:\Users\Person\private" });
        False(report.CanDictate);
        Equal(WhisperReadinessState.Blocked, report.PrimaryBlocker!.State);
        False(report.PrimaryBlocker.Detail.Contains("Person", StringComparison.Ordinal));
    }),
    ("auto-send with no approved application is surfaced as setup", () =>
    {
        WhisperReadinessReport report = WhisperReadinessEvaluator.Evaluate(
            ReadyInputs() with { AutoSendEnabled = true, EnabledAutoSendProfileCount = 0 });
        True(report.CanDictate);
        Equal("auto-send", report.PrimaryBlocker!.Id);
    }),
    ("target inspection is not required to dictate", () =>
        True(WhisperReadinessEvaluator.Evaluate(
            ReadyInputs() with { TargetInspectionAvailable = false }).CanDictate)),

    // ---------- Overlay presentation ----------

    ("an idle session hides the overlay", () =>
        False(WhisperOverlayPresenter.Project(Overlay(WhisperSessionState.Idle)).IsVisible)),
    ("listening shows the meter, the target, and one cancel action", () =>
    {
        WhisperOverlayView view = WhisperOverlayPresenter.Project(Overlay(WhisperSessionState.Listening));
        Equal(WhisperOverlayState.Listening, view.State);
        Equal(WhisperOverlayTone.Accent, view.Tone);
        True(view.ShowLevelMeter);
        True(view.CancelAvailable);
        Equal("chat", view.TargetLabel);
    }),
    ("the hands-free warning changes tone before the limit", () =>
    {
        WhisperOverlayView warning = WhisperOverlayPresenter.Project(Overlay(
            WhisperSessionState.Listening,
            mode: WhisperCaptureMode.HandsFree,
            duration: WhisperDurationState.Warning));
        Equal(WhisperOverlayTone.Warning, warning.Tone);
        True(warning.ShowElapsed);

        WhisperOverlayView expired = WhisperOverlayPresenter.Project(Overlay(
            WhisperSessionState.Listening,
            mode: WhisperCaptureMode.HandsFree,
            duration: WhisperDurationState.Expired));
        Equal(WhisperOverlayTone.Danger, expired.Tone);
    }),
    ("cancel is withdrawn once insertion begins", () =>
        False(WhisperOverlayPresenter.Project(Overlay(WhisperSessionState.Delivering)).CancelAvailable)),
    ("a copy fallback is shown as attention with a recovery action", () =>
    {
        WhisperOverlayView view = WhisperOverlayPresenter.Project(Overlay(
            WhisperSessionState.Completed, delivery: WhisperDeliveryKind.CopyText));
        Equal(WhisperOverlayState.CopiedFallback, view.State);
        Equal(WhisperOverlayTone.Warning, view.Tone);
        Equal("Paste", view.ActionLabel);
    }),
    ("a submitted session reads as confirmed", () =>
    {
        WhisperOverlayView view = WhisperOverlayPresenter.Project(Overlay(
            WhisperSessionState.Completed, delivery: WhisperDeliveryKind.InsertAndSubmit));
        Equal(WhisperOverlayState.Submitted, view.State);
        Equal(WhisperOverlayTone.Signal, view.Tone);
    }),
    ("an unknown target is labelled rather than blank", () =>
    {
        WhisperOverlayView view = WhisperOverlayPresenter.Project(
            Overlay(WhisperSessionState.Listening) with { TargetIsKnown = false });
        Equal("Unknown target", view.TargetLabel);
    }),
    ("elapsed time is formatted for speech-length sessions", () =>
    {
        Equal("0:07", WhisperOverlayPresenter.FormatElapsed(TimeSpan.FromSeconds(7)));
        Equal("19:00", WhisperOverlayPresenter.FormatElapsed(TimeSpan.FromMinutes(19)));
        Equal("1:00:01", WhisperOverlayPresenter.FormatElapsed(TimeSpan.FromSeconds(3601)));
    }),

    // ---------- Diagnostics ----------

    ("diagnostics redact paths, URLs, and quoted payloads", () =>
    {
        string sanitized = WhisperRedaction.Sanitize(
            "provider https://api.example.com rejected \"my private sentence\" at C:\\Users\\me\\a.log",
            200);
        False(sanitized.Contains("api.example.com", StringComparison.Ordinal));
        False(sanitized.Contains("private", StringComparison.Ordinal));
        False(sanitized.Contains("Users", StringComparison.Ordinal));
        True(sanitized.Contains("[redacted]", StringComparison.Ordinal));
    }),
    ("diagnostic detail is bounded", () =>
        True(WhisperRedaction.Sanitize(new string('a', 500), 64).Length <= 64)),
    ("durations are reported as buckets, not exact values", () =>
    {
        Equal(WhisperDurationBucket.UnderFiveSeconds, WhisperRedaction.ToBucket(TimeSpan.FromSeconds(2)));
        Equal(WhisperDurationBucket.UnderTwoMinutes, WhisperRedaction.ToBucket(TimeSpan.FromSeconds(90)));
        Equal(WhisperDurationBucket.Extended, WhisperRedaction.ToBucket(TimeSpan.FromMinutes(15)));
    }),
    ("the diagnostic log is bounded and content-free", () =>
    {
        WhisperDiagnosticLog log = new(2);
        for (int index = 0; index < 5; index++)
        {
            log.Record(new WhisperDiagnosticEvent(
                DateTimeOffset.UnixEpoch,
                WhisperDiagnosticEventKind.SessionStarted,
                WhisperCaptureMode.PushToTalk,
                WhisperTargetKind.PlainText,
                "started"));
        }

        Equal(2, log.CreateSnapshot().Count);
        True(log.CreateSnapshot()[0].ToEvidenceLine().Contains("kind=SessionStarted", StringComparison.Ordinal));
    }),

    // ---------- Scratchpad ----------

    ("scratchpad append and undo are reversible", () =>
    {
        WhisperScratchpadTab tab = new("Note");
        tab.Append("first");
        tab.Append("second");
        Equal("first second", tab.Content);
        True(tab.Undo());
        Equal("first", tab.Content);
        True(tab.Redo());
        Equal("first second", tab.Content);
    }),
    ("scratchpad undo depth is bounded", () =>
    {
        WhisperScratchpadTab tab = new("Note");
        for (int index = 0; index < WhisperScratchpadTab.MaximumUndoDepth + 20; index++)
        {
            tab.Append("x");
        }

        int undone = 0;
        while (tab.Undo())
        {
            undone++;
        }

        Equal(WhisperScratchpadTab.MaximumUndoDepth, undone);
    }),
    ("the scratchpad holds at most five tabs and always keeps one", () =>
    {
        WhisperScratchpad scratchpad = new();
        while (scratchpad.CanAddTab)
        {
            scratchpad.AddTab();
        }

        Equal(WhisperScratchpad.MaximumTabs, scratchpad.Tabs.Count);
        Throws<InvalidOperationException>(() => scratchpad.AddTab());

        for (int index = WhisperScratchpad.MaximumTabs - 1; index >= 0; index--)
        {
            scratchpad.CloseTab(index);
        }

        Equal(1, scratchpad.Tabs.Count);
    }),

    // ---------- Deterministic providers ----------

    ("the deterministic transcriber replays scripted transcripts and records context", () =>
    {
        WhisperDeterministicCaptureSource capture = new(TimeSpan.FromSeconds(2));
        WhisperDeterministicTranscriber transcriber = new("first", "second");
        WhisperAudioClip clip = Wait(capture.CaptureAsync(
            WhisperCaptureMode.PushToTalk, CancellationToken.None));
        WhisperTranscriptionContext context = new(
            WhisperCaptureMode.PushToTalk, "en", "chat", "Message", ["Soltex"]);

        Equal("first", Wait(transcriber.TranscribeAsync(clip, context, CancellationToken.None)));
        Equal("second", Wait(transcriber.TranscribeAsync(clip, context, CancellationToken.None)));
        Equal("second", Wait(transcriber.TranscribeAsync(clip, context, CancellationToken.None)));
        Equal(3, transcriber.ObservedContexts.Count);
        Equal("Soltex", transcriber.ObservedContexts[0].DictionaryTerms.First());
        Equal(1, capture.CaptureCount);
    }),
    ("a failing transcriber surfaces as a fault rather than an empty transcript", () =>
    {
        WhisperDeterministicTranscriber transcriber =
            WhisperDeterministicTranscriber.CreateFailing("provider unavailable");
        WhisperAudioClip clip = Wait(new WhisperDeterministicCaptureSource().CaptureAsync(
            WhisperCaptureMode.PushToTalk, CancellationToken.None));
        WhisperTranscriptionContext context = new(
            WhisperCaptureMode.PushToTalk, null, "chat", "Message", []);
        Throws<InvalidOperationException>(() => Wait(
            transcriber.TranscribeAsync(clip, context, CancellationToken.None)));
    }),
    ("the scripted inspector can change focus mid-session", () =>
    {
        WhisperScriptedTargetInspector inspector = new(
            Snap("chat", "el-1"),
            Snap("mail", "el-2", processId: 42));
        Equal("chat", Wait(inspector.InspectAsync(CancellationToken.None))?.Context.ProcessName);
        Equal("mail", Wait(inspector.InspectAsync(CancellationToken.None))?.Context.ProcessName);
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

// The deterministic providers all complete synchronously, so unwrapping the
// ValueTask here cannot deadlock and keeps the suite a plain list of Actions.
static T Wait<T>(ValueTask<T> pending) => pending.GetAwaiter().GetResult();

static WhisperTargetSnapshot Snap(
    string processName,
    string elementRuntimeId,
    int processId = 7,
    WhisperTargetKind kind = WhisperTargetKind.PlainText) => new(
        new WhisperTargetIdentity(processId, processName, elementRuntimeId),
        Edit(processName, kind));

static WhisperInsertionVerification Verified() => new(
    Verified: true,
    WhisperVerificationMethod.AutomationValueRead,
    "read back");

static WhisperTextDeliveryRequest InsertRequest(WhisperTargetSnapshot capturedTarget) => new(
    new WhisperDeliveryDecision(
        WhisperDeliveryKind.InsertText,
        "hello",
        WhisperSubmitOrigin.None,
        RestoreClipboard: true,
        "insert"),
    capturedTarget);

static WhisperSubmitAuthorization Gate(
    WhisperTargetSnapshot? current,
    WhisperInsertionVerification verification,
    bool warningAccepted = true,
    bool cancelled = false)
{
    WhisperDeliveryDecision decision = new(
        WhisperDeliveryKind.InsertAndSubmit,
        "hello",
        WhisperSubmitOrigin.TerminalPhrase,
        RestoreClipboard: false,
        "authorized");
    return WhisperSubmitGate.Evaluate(
        decision,
        Snap("chat", "el-1"),
        current,
        verification,
        warningAccepted,
        cancelled);
}

static WhisperReadinessInputs ReadyInputs() => new(
    FeatureEnabled: true,
    MicrophoneSelected: true,
    MicrophonePermissionGranted: true,
    ShortcutsRegistered: true,
    ShortcutRegistrationError: null,
    TranscriberConfigured: true,
    TranscriberCredentialAvailable: true,
    TargetInspectionAvailable: true,
    AutoSendEnabled: false,
    AutoSendWarningAccepted: false,
    EnabledAutoSendProfileCount: 0);

static WhisperOverlayInputs Overlay(
    WhisperSessionState state,
    WhisperCaptureMode mode = WhisperCaptureMode.PushToTalk,
    WhisperDurationState duration = WhisperDurationState.Current,
    WhisperDeliveryKind delivery = WhisperDeliveryKind.InsertText) => new(
        new WhisperSessionSnapshot(state, mode, DateTimeOffset.UnixEpoch, null),
        mode,
        "chat",
        TargetIsKnown: true,
        HandsFreeLocked: false,
        TimeSpan.FromSeconds(12),
        duration,
        delivery,
        ErrorDetail: null);

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
