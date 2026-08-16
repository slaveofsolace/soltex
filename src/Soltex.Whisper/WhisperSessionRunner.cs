namespace Soltex.Whisper;

/// <summary>
/// One provider-neutral dictation request. Every outward policy input is explicit;
/// the runner does not infer a target, profile, submission permission, or language.
/// </summary>
public sealed record WhisperSessionRunRequest(
    WhisperCaptureMode Mode,
    WhisperSettings Settings,
    IReadOnlyCollection<WhisperSnippet> Snippets,
    WhisperAppProfile? Profile = null,
    WhisperTextOptions? TextOptions = null,
    bool DedicatedSubmitShortcut = false,
    bool SoltexIsElevated = false);

/// <summary>
/// One completed in-memory result. Transcript-bearing objects stay in the caller's
/// owned session path and are never projected into diagnostics.
/// </summary>
public sealed record WhisperSessionRunResult(
    WhisperFinalizationResult Finalization,
    WhisperTextDeliveryResult Delivery,
    WhisperVerifiedSubmitResult? Submission,
    WhisperTargetSnapshot? CapturedTarget,
    WhisperDeliveryKind PresentationDeliveryKind);

/// <summary>
/// Content-free state emitted for the listening surface.
/// </summary>
public sealed class WhisperSessionStateChangedEventArgs(
    WhisperSessionSnapshot session,
    WhisperCaptureMode mode,
    WhisperTargetKind targetKind,
    string? targetProcessName,
    WhisperDeliveryKind deliveryKind) : EventArgs
{
    public WhisperSessionSnapshot Session { get; } = session;

    public WhisperCaptureMode Mode { get; } = mode;

    public WhisperTargetKind TargetKind { get; } = targetKind;

    public string? TargetProcessName { get; } = targetProcessName;

    public WhisperDeliveryKind DeliveryKind { get; } = deliveryKind;
}

/// <summary>
/// Composes the provider-neutral Whisper interfaces into one serialized session.
/// All insertion and submission decisions remain in the existing core policies.
/// </summary>
public sealed class WhisperSessionRunner : IAsyncDisposable
{
    private readonly IWhisperCaptureSource _capture;
    private readonly IWhisperCaptureControl? _captureControl;
    private readonly IWhisperTranscriber _transcriber;
    private readonly IWhisperTargetInspector _targetInspector;
    private readonly IWhisperTextDelivery _delivery;
    private readonly IWhisperVerifiedSubmitter _submitter;
    private readonly WhisperCoordinator _coordinator;
    private readonly WhisperDiagnosticLog _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly WhisperSessionController _session = new();
    private CancellationTokenSource? _activeCancellation;
    private string? _lastCompletedTranscript;
    private int _captureCompletionRequested;
    private int _disposeStarted;

    public WhisperSessionRunner(
        IWhisperCaptureSource capture,
        IWhisperTranscriber transcriber,
        IWhisperTargetInspector targetInspector,
        IWhisperTextDelivery delivery,
        IWhisperVerifiedSubmitter submitter,
        WhisperCoordinator? coordinator = null,
        WhisperDiagnosticLog? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(transcriber);
        ArgumentNullException.ThrowIfNull(targetInspector);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(submitter);

        _capture = capture;
        _captureControl = capture as IWhisperCaptureControl;
        _transcriber = transcriber;
        _targetInspector = targetInspector;
        _delivery = delivery;
        _submitter = submitter;
        _coordinator = coordinator ?? new WhisperCoordinator();
        _diagnostics = diagnostics ?? new WhisperDiagnosticLog();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<WhisperSessionStateChangedEventArgs>? StateChanged;

    public WhisperSessionSnapshot CreateSnapshot() => _session.CreateSnapshot();

    public IReadOnlyList<WhisperDiagnosticEvent> CreateDiagnosticSnapshot() =>
        _diagnostics.CreateSnapshot();

    public string? LastCompletedTranscript
    {
        get
        {
            lock (_stateLock)
            {
                return _lastCompletedTranscript;
            }
        }
    }

    public bool HasActiveSession
    {
        get
        {
            lock (_stateLock)
            {
                return _activeCancellation is not null;
            }
        }
    }

    public bool CompleteCapture()
    {
        if (_session.CreateSnapshot().State != WhisperSessionState.Listening)
        {
            return false;
        }

        if (_captureControl?.CompleteCurrentCapture() == true)
        {
            return true;
        }

        if (!HasActiveSession)
        {
            return false;
        }

        Interlocked.Exchange(ref _captureCompletionRequested, 1);
        return true;
    }

    public bool CancelActive()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _activeCancellation;
        }

        if (cancellation is null)
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public async ValueTask<WhisperSessionRunResult> RunAsync(
        WhisperSessionRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(request.Snippets);
        if (!Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The requested capture mode is not supported.");
        }

        if (!request.Settings.Enabled)
        {
            throw new InvalidOperationException(
                "Whisper is disabled in the validated settings.");
        }

        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeStarted) != 0,
            this);

        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Whisper already has an active session.");
        }

        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            _runGate.Release();
            throw new ObjectDisposedException(nameof(WhisperSessionRunner));
        }

        using CancellationTokenSource ownedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        WhisperTargetSnapshot? capturedTarget = null;
        WhisperDeliveryKind presentationKind = WhisperDeliveryKind.None;
        WhisperCaptureMode mode = request.Mode;
        try
        {
            PrepareSession(ownedCancellation);
            DateTimeOffset startedAtUtc = _timeProvider.GetUtcNow();
            _session.BeginListening(mode, startedAtUtc);
            Record(
                WhisperDiagnosticEventKind.SessionStarted,
                mode,
                WhisperTargetKind.Unknown,
                "session-started");
            Publish(mode, capturedTarget, presentationKind);

            Task<WhisperAudioClip> captureTask = _capture
                .CaptureAsync(mode, ownedCancellation.Token)
                .AsTask();
            if (Interlocked.Exchange(ref _captureCompletionRequested, 0) != 0)
            {
                _ = _captureControl?.CompleteCurrentCapture();
            }

            Task<WhisperTargetSnapshot?> targetTask = _targetInspector
                .InspectAsync(ownedCancellation.Token)
                .AsTask();

            WhisperAudioClip audio;
            try
            {
                capturedTarget = await targetTask.ConfigureAwait(false);
                audio = await captureTask.ConfigureAwait(false);
            }
            catch
            {
                ownedCancellation.Cancel();
                await DrainTargetAsync(targetTask).ConfigureAwait(false);
                await DrainCaptureAsync(captureTask).ConfigureAwait(false);
                throw;
            }

            Record(
                WhisperDiagnosticEventKind.TargetInspected,
                mode,
                capturedTarget?.Context.Kind ?? WhisperTargetKind.Unknown,
                capturedTarget is null ? "target-unavailable" : "target-confirmed");
            Publish(mode, capturedTarget, presentationKind);

            string transcript;
            using (audio)
            {
                ownedCancellation.Token.ThrowIfCancellationRequested();
                _session.MarkTranscribing();
                Publish(mode, capturedTarget, presentationKind);

                try
                {
                    transcript = await _transcriber.TranscribeAsync(
                        audio,
                        CreateTranscriptionContext(request, capturedTarget),
                        ownedCancellation.Token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        throw new InvalidDataException(
                            "The transcription provider returned no transcript.");
                    }

                    Record(
                        WhisperDiagnosticEventKind.TranscriptionSucceeded,
                        mode,
                        capturedTarget?.Context.Kind ?? WhisperTargetKind.Unknown,
                        "provider-completed");
                }
                catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    Record(
                        WhisperDiagnosticEventKind.TranscriptionFailed,
                        mode,
                        capturedTarget?.Context.Kind ?? WhisperTargetKind.Unknown,
                        "provider-failed");
                    throw;
                }
            }

            ownedCancellation.Token.ThrowIfCancellationRequested();
            _session.MarkProcessing();
            Publish(mode, capturedTarget, presentationKind);

            WhisperFinalizationResult finalization = _coordinator.Finalize(
                new WhisperFinalizationRequest(
                    transcript,
                    capturedTarget?.Context ?? WhisperTargetContext.Unknown,
                    request.Profile,
                    request.Snippets,
                    request.TextOptions ?? WhisperTextOptions.Default,
                    request.Settings.AutoSendEnabled,
                    request.DedicatedSubmitShortcut,
                    request.SoltexIsElevated),
                _timeProvider.GetUtcNow());

            WhisperTextDeliveryResult deliveryResult = NoDelivery();
            WhisperVerifiedSubmitResult? submission = null;
            if (finalization.Delivery.Kind != WhisperDeliveryKind.None)
            {
                _session.MarkDelivering();
                Publish(mode, capturedTarget, presentationKind);
                deliveryResult = await _delivery.DeliverAsync(
                    new WhisperTextDeliveryRequest(
                        finalization.Delivery,
                        capturedTarget,
                        request.SoltexIsElevated),
                    ownedCancellation.Token).ConfigureAwait(false);
                RecordDelivery(mode, capturedTarget, deliveryResult);

                if (finalization.Delivery.Kind is (
                        WhisperDeliveryKind.InsertAndSubmit or
                        WhisperDeliveryKind.SubmitOnly) &&
                    capturedTarget is not null)
                {
                    ownedCancellation.Token.ThrowIfCancellationRequested();
                    submission = await _submitter.SubmitAsync(
                        new WhisperVerifiedSubmitRequest(
                            finalization.Delivery,
                            capturedTarget,
                            deliveryResult,
                            request.Settings.AutoSendWarningAccepted),
                        ownedCancellation.Token).ConfigureAwait(false);
                    RecordSubmission(mode, capturedTarget, submission);
                }
            }

            presentationKind = ResolvePresentationKind(
                finalization.Delivery,
                deliveryResult,
                submission);
            _session.Complete();
            if (finalization.Pipeline.Text.Length > 0)
            {
                lock (_stateLock)
                {
                    _lastCompletedTranscript = finalization.Pipeline.Text;
                }
            }

            Publish(mode, capturedTarget, presentationKind);
            return new WhisperSessionRunResult(
                finalization,
                deliveryResult,
                submission,
                capturedTarget,
                presentationKind);
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
            MarkCancelled(mode, capturedTarget, presentationKind);
            throw;
        }
        catch
        {
            MarkFaulted(mode, capturedTarget, presentationKind);
            throw;
        }
        finally
        {
            ownedCancellation.Cancel();
            lock (_stateLock)
            {
                if (ReferenceEquals(_activeCancellation, ownedCancellation))
                {
                    _activeCancellation = null;
                }
            }

            Interlocked.Exchange(ref _captureCompletionRequested, 0);
            _runGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _ = CancelActive();
        await _runGate.WaitAsync().ConfigureAwait(false);
        _runGate.Release();
        _runGate.Dispose();
    }

    private void PrepareSession(CancellationTokenSource cancellation)
    {
        WhisperSessionState state = _session.CreateSnapshot().State;
        if (state is WhisperSessionState.Completed or
            WhisperSessionState.Cancelled or
            WhisperSessionState.Faulted)
        {
            _session.Reset();
        }

        lock (_stateLock)
        {
            _activeCancellation = cancellation;
        }
    }

    private static WhisperTranscriptionContext CreateTranscriptionContext(
        WhisperSessionRunRequest request,
        WhisperTargetSnapshot? target) => new(
        request.Mode,
        request.Settings.Language.LanguageTag,
        target?.Context.ProcessName ?? "unknown",
        request.Profile?.StyleName ?? request.Settings.DefaultStyleName,
        request.Settings.Vocabulary.Terms);

    private void MarkCancelled(
        WhisperCaptureMode mode,
        WhisperTargetSnapshot? target,
        WhisperDeliveryKind deliveryKind)
    {
        if (IsActive(_session.CreateSnapshot().State))
        {
            _session.Cancel();
        }

        Record(
            WhisperDiagnosticEventKind.SessionCancelled,
            mode,
            target?.Context.Kind ?? WhisperTargetKind.Unknown,
            "session-cancelled");
        Publish(mode, target, deliveryKind);
    }

    private void MarkFaulted(
        WhisperCaptureMode mode,
        WhisperTargetSnapshot? target,
        WhisperDeliveryKind deliveryKind)
    {
        const string safeDetail = "Whisper could not finish this session.";
        if (IsActive(_session.CreateSnapshot().State))
        {
            _session.Fail(safeDetail);
        }

        Record(
            WhisperDiagnosticEventKind.SessionFaulted,
            mode,
            target?.Context.Kind ?? WhisperTargetKind.Unknown,
            safeDetail);
        Publish(mode, target, deliveryKind);
    }

    private void RecordDelivery(
        WhisperCaptureMode mode,
        WhisperTargetSnapshot? target,
        WhisperTextDeliveryResult result)
    {
        WhisperDiagnosticEventKind kind = result.Copied ||
            result.FallbackReason != WhisperInsertionFallbackReason.None
            ? WhisperDiagnosticEventKind.InsertionFallback
            : WhisperDiagnosticEventKind.TextInserted;
        Record(
            kind,
            mode,
            target?.Context.Kind ?? WhisperTargetKind.Unknown,
            $"method={result.Method}; fallback={result.FallbackReason}");
    }

    private void RecordSubmission(
        WhisperCaptureMode mode,
        WhisperTargetSnapshot target,
        WhisperVerifiedSubmitResult result) => Record(
        result.EnterDispatched
            ? WhisperDiagnosticEventKind.SubmitAllowed
            : WhisperDiagnosticEventKind.SubmitDenied,
        mode,
        target.Context.Kind,
        $"origin={result.Origin}; outcome={result.Outcome}; verification={result.Verification.Method}");

    private void Record(
        WhisperDiagnosticEventKind kind,
        WhisperCaptureMode mode,
        WhisperTargetKind targetKind,
        string detail) => _diagnostics.Record(new WhisperDiagnosticEvent(
        _timeProvider.GetUtcNow(),
        kind,
        mode,
        targetKind,
        detail));

    private void Publish(
        WhisperCaptureMode mode,
        WhisperTargetSnapshot? target,
        WhisperDeliveryKind deliveryKind)
    {
        EventHandler<WhisperSessionStateChangedEventArgs>? handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        WhisperSessionStateChangedEventArgs args = new(
            _session.CreateSnapshot(),
            mode,
            target?.Context.Kind ?? WhisperTargetKind.Unknown,
            target?.Context.ProcessName,
            deliveryKind);
        foreach (EventHandler<WhisperSessionStateChangedEventArgs> handler in
                 handlers.GetInvocationList().Cast<EventHandler<WhisperSessionStateChangedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // UI observers are isolated from the session and cannot change policy.
            }
        }
    }

    private static bool IsActive(WhisperSessionState state) => state is
        WhisperSessionState.Listening or
        WhisperSessionState.Transcribing or
        WhisperSessionState.Processing or
        WhisperSessionState.Delivering;

    private static async Task DrainTargetAsync(
        Task<WhisperTargetSnapshot?> targetTask)
    {
        try
        {
            _ = await targetTask.ConfigureAwait(false);
        }
        catch
        {
            // The original session exception remains authoritative.
        }
    }

    private static async Task DrainCaptureAsync(Task<WhisperAudioClip> captureTask)
    {
        try
        {
            using WhisperAudioClip clip = await captureTask.ConfigureAwait(false);
        }
        catch
        {
            // The original session exception remains authoritative.
        }
    }

    private static WhisperTextDeliveryResult NoDelivery() => new(
        WhisperInsertionMethod.None,
        WhisperInsertionFallbackReason.None,
        WhisperClipboardRestoreOutcome.NotRequested,
        MutationDispatched: false,
        Copied: false);

    private static WhisperDeliveryKind ResolvePresentationKind(
        WhisperDeliveryDecision decision,
        WhisperTextDeliveryResult delivery,
        WhisperVerifiedSubmitResult? submission)
    {
        if (delivery.Copied)
        {
            return WhisperDeliveryKind.CopyText;
        }

        return decision.Kind switch
        {
            WhisperDeliveryKind.InsertAndSubmit when submission?.EnterDispatched != true =>
                delivery.MutationDispatched
                    ? WhisperDeliveryKind.InsertText
                    : WhisperDeliveryKind.None,
            WhisperDeliveryKind.SubmitOnly when submission?.EnterDispatched != true =>
                WhisperDeliveryKind.None,
            _ => decision.Kind
        };
    }
}
