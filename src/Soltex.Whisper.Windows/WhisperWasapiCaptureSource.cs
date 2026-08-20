using System.Diagnostics;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public sealed class WhisperWasapiCaptureSource :
    IWhisperCaptureSource,
    IWhisperCaptureControl,
    IAsyncDisposable
{
    public const int OutputSampleRateHz = 16_000;
    public const int OutputChannelCount = 1;
    public const int MaximumCaptureBytes =
        OutputSampleRateHz * sizeof(short) * 60 * 20;

    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(50);
    private readonly IWhisperCaptureBackendFactory _factory;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _stopCapture;
    private string? _requestedDeviceId;
    private bool _disposed;

    public WhisperWasapiCaptureSource(string? requestedDeviceId = null)
        : this(new WindowsWasapiCaptureBackendFactory(), requestedDeviceId)
    {
    }

    internal WhisperWasapiCaptureSource(
        IWhisperCaptureBackendFactory factory,
        string? requestedDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ValidateDeviceId(requestedDeviceId);
        _factory = factory;
        _requestedDeviceId = requestedDeviceId;
    }

    public event EventHandler<WhisperInputLevelEventArgs>? InputLevelChanged;

    public event EventHandler<WhisperCaptureSelection>? DeviceSelectionChanged;

    public string? RequestedDeviceId
    {
        get
        {
            lock (_stateLock)
            {
                return _requestedDeviceId;
            }
        }
    }

    public ValueTask<WhisperCaptureDeviceSnapshot> EnumerateDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _factory.EnumerateAsync(cancellationToken);
    }

    public void SelectInputDevice(string? deviceId)
    {
        ValidateDeviceId(deviceId);
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopCapture is not null)
            {
                throw new InvalidOperationException(
                    "The input device cannot change while Whisper is capturing.");
            }

            _requestedDeviceId = deviceId;
        }
    }

    /// <summary>
    /// Ends the current capture successfully. Caller cancellation remains the
    /// discard path; shortcut release uses this explicit completion signal.
    /// </summary>
    public bool CompleteCurrentCapture()
    {
        lock (_stateLock)
        {
            if (_stopCapture is null)
            {
                return false;
            }

            _stopCapture.Cancel();
            return true;
        }
    }

    public async ValueTask<WhisperAudioClip> CaptureAsync(
        WhisperCaptureMode mode,
        CancellationToken cancellationToken)
    {
        _ = mode;
        ThrowIfDisposed();
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        CancellationTokenSource stopCapture = new();
        lock (_stateLock)
        {
            if (_disposed)
            {
                stopCapture.Dispose();
                _captureGate.Release();
                throw new ObjectDisposedException(nameof(WhisperWasapiCaptureSource));
            }

            _stopCapture = stopCapture;
        }

        using BoundedPcmAccumulator audio = new(MaximumCaptureBytes);
        try
        {
            string? requestedDeviceId = RequestedDeviceId;
            bool recoveredFromDisconnect = false;
            long lastMeterTimestamp = 0;

            while (audio.Remaining > 0)
            {
                await using IWhisperCaptureBackend backend =
                    await _factory.OpenAsync(requestedDeviceId, cancellationToken)
                        .ConfigureAwait(false);
                DeviceSelectionChanged?.Invoke(this, backend.Selection);

                using CancellationTokenSource readCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _disposeCancellation.Token,
                        stopCapture.Token);

                try
                {
                    while (audio.Remaining > 0)
                    {
                        using WhisperPcmPacket packet =
                            await backend.ReadAsync(readCancellation.Token).ConfigureAwait(false);
                        ReadOnlySpan<byte> pcm = packet.Memory.Span;
                        if (pcm.Length > audio.Remaining)
                        {
                            pcm = pcm[..(audio.Remaining & ~1)];
                        }

                        audio.Append(pcm);
                        PublishInputLevel(pcm, ref lastMeterTimestamp);
                    }

                    break;
                }
                catch (OperationCanceledException) when (
                    stopCapture.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested &&
                    !_disposeCancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (WhisperCaptureException exception) when (
                    exception.Kind == WhisperCaptureFailureKind.DeviceDisconnected &&
                    !recoveredFromDisconnect &&
                    !stopCapture.IsCancellationRequested)
                {
                    recoveredFromDisconnect = true;
                    requestedDeviceId = null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            _disposeCancellation.Token.ThrowIfCancellationRequested();
            if (audio.Length == 0)
            {
                throw new WhisperCaptureException(
                    WhisperCaptureFailureKind.NoAudioCaptured,
                    "The microphone returned no audio before capture stopped.");
            }

            int byteLength = audio.Length;
            byte[] ownedPcm = audio.Detach();
            TimeSpan duration = TimeSpan.FromSeconds(
                byteLength / (double)(OutputSampleRateHz * sizeof(short)));
            return WhisperAudioClip.CreateOwned(
                ownedPcm,
                OutputSampleRateHz,
                OutputChannelCount,
                duration);
        }
        finally
        {
            InputLevelChanged?.Invoke(this, new WhisperInputLevelEventArgs(0));
            lock (_stateLock)
            {
                if (ReferenceEquals(_stopCapture, stopCapture))
                {
                    _stopCapture = null;
                }
            }

            stopCapture.Dispose();
            _captureGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposeCancellation.Cancel();
            _stopCapture?.Cancel();
        }

        await _captureGate.WaitAsync().ConfigureAwait(false);
        _captureGate.Release();
        _captureGate.Dispose();
        _disposeCancellation.Dispose();
    }

    private void PublishInputLevel(ReadOnlySpan<byte> pcm, ref long lastTimestamp)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastTimestamp != 0 &&
            Stopwatch.GetElapsedTime(lastTimestamp, now) < MeterInterval)
        {
            return;
        }

        lastTimestamp = now;
        double sumSquares = 0;
        int sampleCount = pcm.Length / sizeof(short);
        for (int index = 0; index < pcm.Length; index += sizeof(short))
        {
            short sample = (short)(pcm[index] | (pcm[index + 1] << 8));
            double normalized = sample / 32768d;
            sumSquares += normalized * normalized;
        }

        double rootMeanSquare = sampleCount == 0
            ? 0
            : Math.Sqrt(sumSquares / sampleCount);
        InputLevelChanged?.Invoke(
            this,
            new WhisperInputLevelEventArgs(Math.Min(1, rootMeanSquare * 3)));
    }

    private static void ValidateDeviceId(string? deviceId)
    {
        if (deviceId is not null &&
            (deviceId.Length == 0 ||
             deviceId.Length > WhisperSettings.MaximumDeviceIdCharacters ||
             deviceId.Any(char.IsControl)))
        {
            throw new ArgumentException(
                "The input-device identifier must be a bounded printable value.",
                nameof(deviceId));
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
