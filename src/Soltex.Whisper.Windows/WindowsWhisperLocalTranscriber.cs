using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Soltex.Whisper.Windows;

public enum WhisperLocalTranscriptionResultCategory
{
    Succeeded,
    Cancelled,
    Failed
}

public enum WhisperLocalTranscriptionFailureKind
{
    None,
    InvalidAudio,
    InvalidContext,
    ModelUnavailable,
    ModelBusy,
    RuntimeUnavailable,
    RuntimeFault,
    EmptyResult,
    OutputTooLarge
}

/// <summary>
/// A stable, content-free local-provider failure. Raw native messages are never
/// carried through this boundary because they may contain model paths or text.
/// </summary>
public sealed class WhisperLocalTranscriptionException : InvalidOperationException
{
    public WhisperLocalTranscriptionException(
        WhisperLocalTranscriptionFailureKind kind,
        string message)
        : base(WhisperRedaction.Sanitize(message, 200))
    {
        if (kind == WhisperLocalTranscriptionFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    public WhisperLocalTranscriptionFailureKind Kind { get; }
}

/// <summary>
/// Provider evidence that deliberately contains no audio, transcript, vocabulary,
/// process, style, target, path, native message, or timing precise enough to infer
/// utterance length.
/// </summary>
public sealed record WhisperLocalTranscriptionDiagnostic(
    DateTimeOffset ObservedAtUtc,
    WhisperDurationBucket DurationBucket,
    string ProviderId,
    string ModelId,
    string RuntimeId,
    WhisperLocalTranscriptionResultCategory Result,
    WhisperLocalTranscriptionFailureKind Failure);

internal sealed record WhisperLocalRuntimeOptions(string Language, string? Prompt);

internal interface IWhisperLocalRuntimeFactory
{
    ValueTask<IWhisperLocalRuntime> CreateAsync(
        WhisperVerifiedModelLease model,
        CancellationToken cancellationToken);
}

internal interface IWhisperLocalRuntime : IAsyncDisposable
{
    IAsyncEnumerable<string> TranscribeAsync(
        Stream waveStream,
        WhisperLocalRuntimeOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Real fully-local Whisper.net adapter. One runtime and at most one processor are
/// retained lazily, while all native access is serialized through a single gate.
/// </summary>
public sealed class WindowsWhisperLocalTranscriber : IWhisperTranscriber, IAsyncDisposable
{
    public const int MaximumPromptCharacters = 4_096;
    public const int MinimumPcmSampleCount = 201;
    public const int MaximumDiagnosticEvents = 100;

    private readonly IWhisperLocalModelSource _modelSource;
    private readonly IWhisperLocalRuntimeFactory _runtimeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _diagnosticLock = new();
    private readonly Queue<WhisperLocalTranscriptionDiagnostic> _diagnostics = [];
    private readonly TaskCompletionSource<bool> _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IWhisperLocalRuntime? _runtime;
    private int _disposeStarted;

    public WindowsWhisperLocalTranscriber(WindowsWhisperLocalModelManager modelManager)
        : this(modelManager, new WhisperNetLocalRuntimeFactory(), TimeProvider.System)
    {
    }

    internal WindowsWhisperLocalTranscriber(
        IWhisperLocalModelSource modelSource,
        IWhisperLocalRuntimeFactory runtimeFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(modelSource);
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        _modelSource = modelSource;
        _runtimeFactory = runtimeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ReadOnlyCollection<WhisperLocalTranscriptionDiagnostic> CreateDiagnosticSnapshot()
    {
        lock (_diagnosticLock)
        {
            return Array.AsReadOnly(_diagnostics.ToArray());
        }
    }

    public async ValueTask<string> TranscribeAsync(
        WhisperAudioClip audio,
        WhisperTranscriptionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfDisposed();

        WhisperDurationBucket durationBucket = WhisperRedaction.ToBucket(audio.Duration);
        WhisperLocalRuntimeOptions options;
        try
        {
            ValidateAudio(audio);
            options = CreateRuntimeOptions(context);
        }
        catch (WhisperLocalTranscriptionException exception)
        {
            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Failed, exception.Kind);
            throw;
        }

        bool gateHeld = false;
        try
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            ThrowIfDisposed();
            _runtime ??= await CreateRuntimeAsync(cancellationToken).ConfigureAwait(false);

            using WhisperPcmWaveStream wave = new(
                audio.Pcm16,
                audio.SampleRateHz,
                checked((short)audio.ChannelCount));
            StringBuilder transcript = new();
            await foreach (string segment in _runtime
                               .TranscribeAsync(wave, options, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = segment ?? string.Empty;
                if (transcript.Length > WhisperLimits.MaximumTranscriptCharacters - text.Length)
                {
                    throw Failure(
                        WhisperLocalTranscriptionFailureKind.OutputTooLarge,
                        "The local transcription exceeded the bounded text limit.");
                }

                transcript.Append(text);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string completed = transcript.ToString().Trim();
            if (completed.Length == 0)
            {
                throw Failure(
                    WhisperLocalTranscriptionFailureKind.EmptyResult,
                    "The local transcription runtime returned no text.");
            }

            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Succeeded,
                WhisperLocalTranscriptionFailureKind.None);
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Cancelled,
                WhisperLocalTranscriptionFailureKind.None);
            throw;
        }
        catch (WhisperLocalTranscriptionException exception)
        {
            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Failed, exception.Kind);
            throw;
        }
        catch (WhisperModelInstallException exception)
        {
            WhisperLocalTranscriptionFailureKind kind = exception.Kind == WhisperModelFailureKind.Busy
                ? WhisperLocalTranscriptionFailureKind.ModelBusy
                : WhisperLocalTranscriptionFailureKind.ModelUnavailable;
            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Failed, kind);
            throw Failure(kind, kind == WhisperLocalTranscriptionFailureKind.ModelBusy
                ? "The local model is busy with another owned operation."
                : "The verified local transcription model is unavailable.");
        }
        catch (Exception)
        {
            await ResetRuntimeAsync().ConfigureAwait(false);
            Record(durationBucket, WhisperLocalTranscriptionResultCategory.Failed,
                WhisperLocalTranscriptionFailureKind.RuntimeFault);
            throw Failure(
                WhisperLocalTranscriptionFailureKind.RuntimeFault,
                "The local transcription runtime could not complete this request.");
        }
        finally
        {
            if (gateHeld)
            {
                _operationGate.Release();
            }
        }
    }

    /// <summary>
    /// Releases native model memory so an explicit model repair or deletion can proceed
    /// without waiting for application shutdown.
    /// </summary>
    public async ValueTask UnloadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await ResetRuntimeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ResetRuntimeAsync().ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
                _operationGate.Dispose();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (failure is null)
            {
                _disposeCompletion.TrySetResult(true);
            }
            else
            {
                _disposeCompletion.TrySetException(failure);
            }

            GC.SuppressFinalize(this);
        }
    }

    private async ValueTask<IWhisperLocalRuntime> CreateRuntimeAsync(
        CancellationToken cancellationToken)
    {
        await using WhisperVerifiedModelLease model =
            await _modelSource.OpenVerifiedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IWhisperLocalRuntime runtime = await _runtimeFactory
                .CreateAsync(model, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return runtime;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WhisperLocalTranscriptionException)
        {
            throw;
        }
        catch (Exception)
        {
            throw Failure(
                WhisperLocalTranscriptionFailureKind.RuntimeUnavailable,
                "The local CPU transcription runtime could not be initialized.");
        }
    }

    private async ValueTask ResetRuntimeAsync()
    {
        IWhisperLocalRuntime? runtime = _runtime;
        _runtime = null;
        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void ValidateAudio(WhisperAudioClip audio)
    {
        int byteLength = audio.Pcm16.Length;
        if (audio.SampleRateHz != WhisperWasapiCaptureSource.OutputSampleRateHz ||
            audio.ChannelCount != WhisperWasapiCaptureSource.OutputChannelCount ||
            byteLength % sizeof(short) != 0 ||
            byteLength > WhisperWasapiCaptureSource.MaximumCaptureBytes ||
            byteLength / sizeof(short) < MinimumPcmSampleCount)
        {
            throw Failure(
                WhisperLocalTranscriptionFailureKind.InvalidAudio,
                "Local transcription requires bounded 16 kHz mono PCM16 audio.");
        }
    }

    private static WhisperLocalRuntimeOptions CreateRuntimeOptions(
        WhisperTranscriptionContext context)
    {
        string language;
        try
        {
            language = context.PreferredLanguage is null
                ? "auto"
                : CultureInfo.GetCultureInfo(
                    context.PreferredLanguage,
                    predefinedOnly: true).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            throw Failure(
                WhisperLocalTranscriptionFailureKind.InvalidContext,
                "The selected transcription language is not supported.");
        }

        return new WhisperLocalRuntimeOptions(language, CreatePrompt(context.DictionaryTerms));
    }

    private static string? CreatePrompt(IReadOnlyCollection<string> dictionaryTerms)
    {
        ArgumentNullException.ThrowIfNull(dictionaryTerms);
        if (dictionaryTerms.Count == 0)
        {
            return null;
        }

        WhisperVocabulary vocabulary;
        try
        {
            vocabulary = new WhisperVocabulary(dictionaryTerms);
        }
        catch (ArgumentException)
        {
            throw Failure(
                WhisperLocalTranscriptionFailureKind.InvalidContext,
                "The personal vocabulary contains an unsupported recognition hint.");
        }

        StringBuilder prompt = new("Vocabulary: ");
        foreach (string term in vocabulary.Terms)
        {
            int separator = prompt.Length == "Vocabulary: ".Length ? 0 : 2;
            if (prompt.Length + separator + term.Length > MaximumPromptCharacters)
            {
                break;
            }

            if (separator != 0)
            {
                prompt.Append(", ");
            }

            prompt.Append(term);
        }

        return prompt.Length == "Vocabulary: ".Length ? null : prompt.ToString();
    }

    private void Record(
        WhisperDurationBucket durationBucket,
        WhisperLocalTranscriptionResultCategory result,
        WhisperLocalTranscriptionFailureKind failure)
    {
        WhisperLocalTranscriptionDiagnostic diagnostic = new(
            _timeProvider.GetUtcNow(),
            durationBucket,
            WhisperLocalModelDefaults.ProviderId,
            WhisperLocalModelDefaults.ModelId,
            WhisperLocalModelDefaults.RuntimeId,
            result,
            failure);
        lock (_diagnosticLock)
        {
            _diagnostics.Enqueue(diagnostic);
            while (_diagnostics.Count > MaximumDiagnosticEvents)
            {
                _diagnostics.Dequeue();
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref _disposeStarted) != 0,
        this);

    private static WhisperLocalTranscriptionException Failure(
        WhisperLocalTranscriptionFailureKind kind,
        string message) => new(kind, message);
}

internal sealed class WhisperNetLocalRuntimeFactory : IWhisperLocalRuntimeFactory
{
    private static readonly object RuntimeConfigurationLock = new();

    public async ValueTask<IWhisperLocalRuntime> CreateAsync(
        WhisperVerifiedModelLease model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();
        WhisperFactory factory = await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (RuntimeConfigurationLock)
                {
                    RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
                    return WhisperFactory.FromPath(model.ModelPath);
                }
            },
            CancellationToken.None).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            factory.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new WhisperNetLocalRuntime(factory);
    }
}

internal sealed class WhisperNetLocalRuntime(WhisperFactory factory) : IWhisperLocalRuntime
{
    private WhisperProcessor? _processor;
    private WhisperLocalRuntimeOptions? _configuration;
    private bool _disposed;

    public async IAsyncEnumerable<string> TranscribeAsync(
        Stream waveStream,
        WhisperLocalRuntimeOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waveStream);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureProcessorAsync(options, cancellationToken).ConfigureAwait(false);
        WhisperProcessor processor = _processor ??
            throw new InvalidOperationException("The local processor was not initialized.");
        await foreach (SegmentData segment in processor
                           .ProcessAsync(waveStream, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return segment.Text;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        factory.Dispose();
    }

    private async ValueTask EnsureProcessorAsync(
        WhisperLocalRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        if (_processor is not null && _configuration == options)
        {
            return;
        }

        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        WhisperProcessorBuilder builder = factory.CreateBuilder().WithoutStringPool();
        builder = string.Equals(options.Language, "auto", StringComparison.Ordinal)
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(options.Language);
        if (!string.IsNullOrEmpty(options.Prompt))
        {
            builder = builder.WithPrompt(options.Prompt);
        }

        _processor = builder.Build();
        _configuration = options;
        if (cancellationToken.IsCancellationRequested)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
            _configuration = null;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
