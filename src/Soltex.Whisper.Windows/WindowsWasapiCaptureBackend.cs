using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

internal sealed class WindowsWasapiCaptureBackendFactory : IWhisperCaptureBackendFactory
{
    private const int MaximumDeviceCount = 64;
    private const int MaximumDeviceNameCharacters = 96;

    public ValueTask<WhisperCaptureDeviceSnapshot> EnumerateAsync(
        CancellationToken cancellationToken) =>
        new(Task.Run(() => Enumerate(cancellationToken), cancellationToken));

    public ValueTask<IWhisperCaptureBackend> OpenAsync(
        string? requestedDeviceId,
        CancellationToken cancellationToken) =>
        new(Task.Run<IWhisperCaptureBackend>(
            () => Open(requestedDeviceId, cancellationToken),
            cancellationToken));

    private static WhisperCaptureDeviceSnapshot Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IWasapiDeviceEnumerator enumerator = CreateEnumerator();
        try
        {
            string? defaultId = TryGetDefaultId(enumerator);
            int result = enumerator.EnumAudioEndpoints(
                WasapiDataFlow.Capture,
                WasapiDeviceState.Active,
                out IWasapiDeviceCollection collection);
            ThrowForFailure(result, "Windows could not enumerate microphone devices.");

            try
            {
                result = collection.GetCount(out uint observedCount);
                ThrowForFailure(result, "Windows could not count microphone devices.");
                uint count = Math.Min(observedCount, MaximumDeviceCount);
                List<WhisperCaptureDevice> devices = [];
                for (uint index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (collection.Item(index, out IWasapiDevice device) != WasapiNative.Ok)
                    {
                        continue;
                    }

                    try
                    {
                        if (!TryReadId(device, out string? id))
                        {
                            continue;
                        }

                        devices.Add(new WhisperCaptureDevice(
                            id!,
                            ReadFriendlyName(device),
                            string.Equals(id, defaultId, StringComparison.Ordinal)));
                    }
                    finally
                    {
                        WasapiNative.Release(device);
                    }
                }

                return new WhisperCaptureDeviceSnapshot(devices);
            }
            finally
            {
                WasapiNative.Release(collection);
            }
        }
        finally
        {
            WasapiNative.Release(enumerator);
        }
    }

    private static WindowsWasapiCaptureBackend Open(
        string? requestedDeviceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IWasapiDeviceEnumerator enumerator = CreateEnumerator();
        IWasapiDevice? device = null;
        try
        {
            bool usedFallback = false;
            if (!string.IsNullOrEmpty(requestedDeviceId))
            {
                int selectedResult = enumerator.GetDevice(requestedDeviceId, out device);
                if (selectedResult != WasapiNative.Ok ||
                    device.GetState(out uint state) != WasapiNative.Ok ||
                    state != WasapiDeviceState.Active)
                {
                    WasapiNative.Release(device);
                    device = null;
                    usedFallback = true;
                }
            }

            if (device is null)
            {
                int defaultResult = enumerator.GetDefaultAudioEndpoint(
                    WasapiDataFlow.Capture,
                    WasapiDeviceRole.Multimedia,
                    out device);
                if (defaultResult != WasapiNative.Ok)
                {
                    ThrowForFailure(defaultResult, "Windows reported no available microphone.");
                }
            }

            if (!TryReadId(device, out string? actualId))
            {
                throw new WhisperCaptureException(
                    WhisperCaptureFailureKind.NoDevice,
                    "Windows could not identify the selected microphone.");
            }

            WhisperCaptureSelection selection = new(
                actualId!,
                ReadFriendlyName(device),
                usedFallback);
            Guid audioClientId = WasapiNative.AudioClientId;
            int activationResult = device.Activate(
                ref audioClientId,
                WasapiNative.ClassContextAll,
                IntPtr.Zero,
                out object activated);
            ThrowForFailure(activationResult, "Windows could not open the selected microphone.");
            if (activated is not IAudioClient audioClient)
            {
                WasapiNative.Release(activated);
                throw new WhisperCaptureException(
                    WhisperCaptureFailureKind.Unavailable,
                    "Windows returned an incompatible microphone service.");
            }

            try
            {
                return WindowsWasapiCaptureBackend.Start(audioClient, selection);
            }
            catch
            {
                WasapiNative.Release(audioClient);
                throw;
            }
        }
        finally
        {
            WasapiNative.Release(device);
            WasapiNative.Release(enumerator);
        }
    }

    private static IWasapiDeviceEnumerator CreateEnumerator()
    {
        try
        {
            return (IWasapiDeviceEnumerator)(object)new WasapiDeviceEnumeratorObject();
        }
        catch (COMException exception)
        {
            throw MapException(
                exception.HResult,
                "Windows Core Audio could not start.",
                exception);
        }
        catch (InvalidCastException exception)
        {
            throw new WhisperCaptureException(
                WhisperCaptureFailureKind.Unavailable,
                "Windows Core Audio returned an incompatible service.",
                exception);
        }
    }

    private static string? TryGetDefaultId(IWasapiDeviceEnumerator enumerator)
    {
        int result = enumerator.GetDefaultAudioEndpoint(
            WasapiDataFlow.Capture,
            WasapiDeviceRole.Multimedia,
            out IWasapiDevice device);
        if (result != WasapiNative.Ok)
        {
            return null;
        }

        try
        {
            return TryReadId(device, out string? id) ? id : null;
        }
        finally
        {
            WasapiNative.Release(device);
        }
    }

    private static bool TryReadId(IWasapiDevice device, out string? id)
    {
        int result = device.GetId(out string value);
        bool accepted = result == WasapiNative.Ok &&
            !string.IsNullOrWhiteSpace(value) &&
            value.Length <= WhisperSettings.MaximumDeviceIdCharacters &&
            !value.Any(char.IsControl);
        id = accepted ? value : null;
        return accepted;
    }

    private static string ReadFriendlyName(IWasapiDevice device)
    {
        if (device.OpenPropertyStore(WasapiNative.StorageRead, out IWasapiPropertyStore store) !=
            WasapiNative.Ok)
        {
            return "Windows microphone";
        }

        try
        {
            WasapiPropertyKey key = WasapiNative.FriendlyNameKey;
            if (store.GetValue(ref key, out WasapiPropVariant value) != WasapiNative.Ok)
            {
                return "Windows microphone";
            }

            try
            {
                string? name = value.ReadString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return "Windows microphone";
                }

                string bounded = new(name
                    .Where(character => !char.IsControl(character))
                    .Take(MaximumDeviceNameCharacters)
                    .ToArray());
                return string.IsNullOrWhiteSpace(bounded)
                    ? "Windows microphone"
                    : bounded.Trim();
            }
            finally
            {
                _ = WasapiNative.PropVariantClear(ref value);
            }
        }
        finally
        {
            WasapiNative.Release(store);
        }
    }

    internal static void ThrowForFailure(int result, string message)
    {
        if (result < 0)
        {
            throw MapException(result, message);
        }
    }

    internal static WhisperCaptureException MapException(
        int result,
        string message,
        Exception? innerException = null)
    {
        WhisperCaptureFailureKind kind = result switch
        {
            WasapiNative.AccessDenied => WhisperCaptureFailureKind.PermissionDenied,
            WasapiNative.NotFound => WhisperCaptureFailureKind.NoDevice,
            WasapiNative.DeviceInUse => WhisperCaptureFailureKind.DeviceInUse,
            WasapiNative.DeviceInvalidated or WasapiNative.EndpointCreateFailed =>
                WhisperCaptureFailureKind.DeviceDisconnected,
            _ => WhisperCaptureFailureKind.Unavailable
        };
        string safeMessage = kind switch
        {
            WhisperCaptureFailureKind.PermissionDenied =>
                "Windows denied Soltex microphone access.",
            WhisperCaptureFailureKind.NoDevice =>
                "Windows reported no available microphone.",
            WhisperCaptureFailureKind.DeviceInUse =>
                "The selected microphone is in exclusive use by another application.",
            WhisperCaptureFailureKind.DeviceDisconnected =>
                "The selected microphone disconnected.",
            _ => message
        };

        return innerException is null
            ? new WhisperCaptureException(kind, safeMessage)
            : new WhisperCaptureException(kind, safeMessage, innerException);
    }
}

internal sealed class WindowsWasapiCaptureBackend : IWhisperCaptureBackend
{
    private const long SharedBufferDuration = 1_000_000; // 100 ms in 100 ns units.
    private readonly IAudioClient _audioClient;
    private readonly IAudioCaptureClient _captureClient;
    private readonly Pcm16MonoNormalizer _normalizer;
    private bool _disposed;

    private WindowsWasapiCaptureBackend(
        IAudioClient audioClient,
        IAudioCaptureClient captureClient,
        Pcm16MonoNormalizer normalizer,
        WhisperCaptureSelection selection)
    {
        _audioClient = audioClient;
        _captureClient = captureClient;
        _normalizer = normalizer;
        Selection = selection;
    }

    public WhisperCaptureSelection Selection { get; }

    internal static WindowsWasapiCaptureBackend Start(
        IAudioClient audioClient,
        WhisperCaptureSelection selection)
    {
        IntPtr mixFormat = IntPtr.Zero;
        try
        {
            int result = audioClient.GetMixFormat(out mixFormat);
            WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                result,
                "Windows could not read the microphone format.");
            Pcm16MonoNormalizer normalizer = new(mixFormat);
            result = audioClient.Initialize(
                AudioClientShareMode.Shared,
                WasapiNative.StreamFlagsNoPersist,
                SharedBufferDuration,
                0,
                mixFormat,
                IntPtr.Zero);
            WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                result,
                "Windows could not initialize shared microphone capture.");

            Guid captureClientId = WasapiNative.AudioCaptureClientId;
            result = audioClient.GetService(ref captureClientId, out object service);
            WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                result,
                "Windows could not create the microphone capture stream.");
            if (service is not IAudioCaptureClient captureClient)
            {
                WasapiNative.Release(service);
                throw new WhisperCaptureException(
                    WhisperCaptureFailureKind.Unavailable,
                    "Windows returned an incompatible microphone stream.");
            }

            result = audioClient.Start();
            if (result < 0)
            {
                WasapiNative.Release(captureClient);
                WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                    result,
                    "Windows could not start microphone capture.");
            }

            return new WindowsWasapiCaptureBackend(
                audioClient,
                captureClient,
                normalizer,
                selection);
        }
        finally
        {
            if (mixFormat != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormat);
            }
        }
    }

    public async ValueTask<WhisperPcmPacket> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int result = _captureClient.GetNextPacketSize(out uint frameCount);
            WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                result,
                "Windows microphone capture stopped unexpectedly.");
            if (frameCount == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            result = _captureClient.GetBuffer(
                out IntPtr data,
                out frameCount,
                out uint flags,
                out _,
                out _);
            WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                result,
                "Windows could not read the microphone packet.");

            byte[]? normalized = null;
            try
            {
                bool silent = (flags & WasapiNative.BufferFlagsSilent) != 0;
                normalized = _normalizer.Normalize(data, frameCount, silent);
            }
            finally
            {
                int releaseResult = _captureClient.ReleaseBuffer(frameCount);
                if (releaseResult < 0 && normalized is not null)
                {
                    CryptographicOperations.ZeroMemory(normalized);
                    normalized = null;
                }

                WindowsWasapiCaptureBackendFactory.ThrowForFailure(
                    releaseResult,
                    "Windows could not release the microphone packet.");
            }

            if (normalized is null || normalized.Length == 0)
            {
                continue;
            }

            return new WhisperPcmPacket(normalized);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _ = _audioClient.Stop();
        _ = _audioClient.Reset();
        WasapiNative.Release(_captureClient);
        WasapiNative.Release(_audioClient);
        return ValueTask.CompletedTask;
    }
}
