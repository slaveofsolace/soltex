using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Soltex.Audio;

/// <summary>
/// Read-only Windows Core Audio endpoint observation. This provider only reads
/// friendly names, state, default-endpoint identity, and the current master
/// volume/mute reported by Windows; it never sets volume, routes audio, or
/// applies any processing.
/// </summary>
public static class AudioEndpointProvider
{
    public const int MaximumEndpointCount = 32;
    public const int MaximumEndpointNameLength = 80;
    private const int MaximumObservedEndpointCount = 256;
    private const string Provenance =
        "IMMDeviceEnumerator · IMMDevice · IPropertyStore (PKEY_Device_FriendlyName) · IAudioEndpointVolume";

    public static Task<AudioEndpointSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(Capture, cancellationToken);
    }

    private static AudioEndpointSnapshot Capture()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        Stopwatch capture = Stopwatch.StartNew();
        List<string> limitations =
        [
            "Soltex reads volume and mute state only; it does not set volume, route audio, or apply audio processing."
        ];
        List<AudioEndpoint> endpoints = [];
        int inaccessibleEndpointCount = 0;

        IMMDeviceEnumerator? enumerator = CreateEnumerator();
        if (enumerator is null)
        {
            limitations.Add("Windows Core Audio could not be reached (MMDeviceEnumerator activation failed).");
            capture.Stop();
            return new AudioEndpointSnapshot(
                capturedAt, capture.Elapsed, AudioObservationState.Unavailable, endpoints, 0, Provenance, limitations);
        }

        try
        {
            string? defaultRenderId = GetDefaultEndpointId(enumerator, DataFlow.Render);
            string? defaultCaptureId = GetDefaultEndpointId(enumerator, DataFlow.Capture);
            if (defaultRenderId is null)
            {
                limitations.Add("The default playback endpoint could not be identified.");
            }

            if (defaultCaptureId is null)
            {
                limitations.Add("The default recording endpoint could not be identified.");
            }

            bool renderEnumerated = CaptureDirection(
                enumerator, DataFlow.Render, AudioEndpointDirection.Render, defaultRenderId, endpoints, ref inaccessibleEndpointCount);
            bool captureEnumerated = CaptureDirection(
                enumerator, DataFlow.Capture, AudioEndpointDirection.Capture, defaultCaptureId, endpoints, ref inaccessibleEndpointCount);
            if (!renderEnumerated)
            {
                limitations.Add("Playback endpoints could not be enumerated.");
            }

            if (!captureEnumerated)
            {
                limitations.Add("Recording endpoints could not be enumerated.");
            }

            if (inaccessibleEndpointCount > 0)
            {
                limitations.Add("One or more audio endpoints could not be fully observed.");
            }

            AudioObservationState state = ClassifyState(
                renderEnumerated || captureEnumerated, endpoints.Count, inaccessibleEndpointCount);
            capture.Stop();
            return new AudioEndpointSnapshot(
                capturedAt, capture.Elapsed, state, endpoints, inaccessibleEndpointCount, Provenance, limitations);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        try
        {
            return (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorObject();
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static string? GetDefaultEndpointId(IMMDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, DeviceRole.Multimedia, out IMMDevice device);
        if (hr != CoreAudio.Ok)
        {
            return null;
        }

        try
        {
            return TryGetEndpointId(device, out string? id) ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static bool TryGetEndpointId(IMMDevice device, out string? id)
    {
        int hr = device.GetId(out string value);
        id = hr == CoreAudio.Ok ? value : null;
        return id is not null;
    }

    private static bool CaptureDirection(
        IMMDeviceEnumerator enumerator,
        DataFlow dataFlow,
        AudioEndpointDirection direction,
        string? defaultId,
        List<AudioEndpoint> endpoints,
        ref int inaccessibleEndpointCount)
    {
        int hr = enumerator.EnumAudioEndpoints(dataFlow, DeviceStateMask.All, out IMMDeviceCollection collection);
        if (hr != CoreAudio.Ok)
        {
            return false;
        }

        try
        {
            hr = collection.GetCount(out uint count);
            if (hr != CoreAudio.Ok)
            {
                return false;
            }

            uint observedCount = Math.Min(count, MaximumObservedEndpointCount);
            int addedForDirection = 0;
            for (uint index = 0; index < observedCount; index++)
            {
                if (addedForDirection >= MaximumEndpointCount)
                {
                    inaccessibleEndpointCount++;
                    continue;
                }

                if (TryReadEndpoint(collection, index, direction, defaultId, out AudioEndpoint? endpoint))
                {
                    endpoints.Add(endpoint);
                    addedForDirection++;
                }
                else
                {
                    inaccessibleEndpointCount++;
                }
            }

            if (count > MaximumObservedEndpointCount)
            {
                inaccessibleEndpointCount += (int)(count - MaximumObservedEndpointCount);
            }

            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    private static bool TryReadEndpoint(
        IMMDeviceCollection collection,
        uint index,
        AudioEndpointDirection direction,
        string? defaultId,
        [NotNullWhen(true)] out AudioEndpoint? endpoint)
    {
        endpoint = null;
        int hr = collection.Item(index, out IMMDevice device);
        if (hr != CoreAudio.Ok)
        {
            return false;
        }

        try
        {
            hr = device.GetState(out uint stateValue);
            if (hr != CoreAudio.Ok)
            {
                return false;
            }

            bool isDefault = defaultId is not null &&
                TryGetEndpointId(device, out string? id) &&
                string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
            string name = SanitizeName(ReadFriendlyName(device));
            (double? volumeScalar, bool? isMuted) = ReadVolume(device);

            endpoint = new AudioEndpoint(name, direction, MapState(stateValue), isDefault, volumeScalar, isMuted);
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        int hr = device.OpenPropertyStore(CoreAudio.StorageRead, out IPropertyStore store);
        if (hr != CoreAudio.Ok)
        {
            return null;
        }

        try
        {
            PropertyKey key = CoreAudio.FriendlyNameKey;
            hr = store.GetValue(ref key, out PropVariant value);
            if (hr != CoreAudio.Ok)
            {
                return null;
            }

            try
            {
                return value.ReadString();
            }
            finally
            {
                _ = CoreAudio.PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static (double? VolumeScalar, bool? IsMuted) ReadVolume(IMMDevice device)
    {
        Guid volumeId = CoreAudio.AudioEndpointVolumeId;
        int hr = device.Activate(ref volumeId, CoreAudio.ClassContextAll, IntPtr.Zero, out object instance);
        if (hr != CoreAudio.Ok || instance is not IAudioEndpointVolume volume)
        {
            return (null, null);
        }

        try
        {
            double? scalar = volume.GetMasterVolumeLevelScalar(out float level) == CoreAudio.Ok
                ? Math.Clamp(level, 0f, 1f)
                : null;
            bool? muted = volume.GetMute(out bool isMuted) == CoreAudio.Ok
                ? isMuted
                : null;
            return (scalar, muted);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    private static AudioEndpointState MapState(uint state) => state switch
    {
        DeviceStateMask.Active => AudioEndpointState.Active,
        DeviceStateMask.Disabled => AudioEndpointState.Disabled,
        DeviceStateMask.NotPresent => AudioEndpointState.NotPresent,
        DeviceStateMask.Unplugged => AudioEndpointState.Unplugged,
        _ => AudioEndpointState.Unknown
    };

    internal static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unavailable";
        }

        string safe = new string(value
            .Where(character => !char.IsControl(character))
            .Take(MaximumEndpointNameLength)
            .ToArray()).Trim();
        return safe.Length == 0 ? "Unavailable" : safe;
    }

    internal static AudioObservationState ClassifyState(
        bool anyEnumerationSucceeded, int endpointCount, int inaccessibleEndpointCount)
    {
        if (!anyEnumerationSucceeded)
        {
            return AudioObservationState.Unavailable;
        }

        if (endpointCount == 0 && inaccessibleEndpointCount > 0)
        {
            return AudioObservationState.Unavailable;
        }

        return inaccessibleEndpointCount > 0 ? AudioObservationState.Partial : AudioObservationState.Current;
    }
}
