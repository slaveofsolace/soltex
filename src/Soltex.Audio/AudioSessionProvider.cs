using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Soltex.Audio;

/// <summary>
/// Bounded observation of active shared-mode render sessions. The provider
/// reads process names but never executable paths, command lines, session
/// identifiers, or icon paths. Raw Core Audio identifiers are reduced to
/// in-memory hashes used only for just-in-time mutation revalidation.
/// </summary>
public static class AudioSessionProvider
{
    public const int MaximumSessionCount = 24;
    public const int MaximumObservedSessionCount = 128;
    public const int MaximumSessionNameLength = 80;
    public const int MaximumEndpointNameLength = 80;

    private const string Provenance =
        "IMMDeviceEnumerator · IAudioSessionManager2 · IAudioSessionControl2 · ISimpleAudioVolume";

    public static Task<AudioSessionSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Capture(cancellationToken), cancellationToken);
    }

    private static AudioSessionSnapshot Capture(CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        Stopwatch capture = Stopwatch.StartNew();
        List<AudioSession> sessions = [];
        List<string> limitations =
        [
            "Only active shared-mode playback sessions are shown; routing, EQ, effects, and virtual devices are not provided.",
            "Session controls are revalidated against Windows immediately before each write."
        ];
        int observed = 0;
        int inaccessible = 0;
        int omitted = 0;
        bool endpointEnumerationSucceeded = false;

        IMMDeviceEnumerator? deviceEnumerator = CreateEnumerator();
        if (deviceEnumerator is null)
        {
            capture.Stop();
            limitations.Add("Windows Core Audio session discovery could not be reached.");
            return BuildSnapshot(
                capturedAt, capture.Elapsed, AudioObservationState.Unavailable, sessions,
                observed, inaccessible, omitted, Provenance, limitations);
        }

        try
        {
            int hr = deviceEnumerator.EnumAudioEndpoints(
                DataFlow.Render,
                DeviceStateMask.Active,
                out IMMDeviceCollection devices);
            if (!CoreAudio.Succeeded(hr) || devices is null)
            {
                capture.Stop();
                limitations.Add("Active playback endpoints could not be enumerated.");
                return BuildSnapshot(
                    capturedAt, capture.Elapsed, AudioObservationState.Unavailable, sessions,
                    observed, inaccessible, omitted, Provenance, limitations);
            }

            try
            {
                hr = devices.GetCount(out uint endpointCount);
                if (!CoreAudio.Succeeded(hr))
                {
                    capture.Stop();
                    limitations.Add("The playback endpoint count could not be read.");
                    return BuildSnapshot(
                        capturedAt, capture.Elapsed, AudioObservationState.Unavailable, sessions,
                        observed, inaccessible, omitted, Provenance, limitations);
                }

                endpointEnumerationSucceeded = true;

                uint boundedEndpointCount = Math.Min(
                    endpointCount,
                    (uint)AudioEndpointProvider.MaximumEndpointCount);
                omitted += endpointCount > boundedEndpointCount
                    ? checked((int)(endpointCount - boundedEndpointCount))
                    : 0;

                for (uint endpointIndex = 0; endpointIndex < boundedEndpointCount; endpointIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (observed >= MaximumObservedSessionCount)
                    {
                        omitted++;
                        break;
                    }

                    if (!TryReadEndpoint(
                            devices,
                            endpointIndex,
                            sessions,
                            ref observed,
                            ref inaccessible,
                            ref omitted,
                            cancellationToken))
                    {
                        inaccessible++;
                    }
                }
            }
            finally
            {
                CoreAudio.ReleaseComObject(devices);
            }
        }
        finally
        {
            CoreAudio.ReleaseComObject(deviceEnumerator);
        }

        if (inaccessible > 0)
        {
            limitations.Add("One or more session records could not be fully observed.");
        }

        if (omitted > 0)
        {
            limitations.Add("The bounded session result omitted one or more additional records.");
        }

        capture.Stop();
        AudioObservationState state = ClassifyState(
            anyEndpointEnumerationSucceeded: endpointEnumerationSucceeded,
            inaccessibleSessionCount: inaccessible,
            omittedSessionCount: omitted);
        return BuildSnapshot(
            capturedAt, capture.Elapsed, state, sessions, observed, inaccessible, omitted, Provenance, limitations);
    }

    private static bool TryReadEndpoint(
        IMMDeviceCollection devices,
        uint endpointIndex,
        List<AudioSession> sessions,
        ref int observed,
        ref int inaccessible,
        ref int omitted,
        CancellationToken cancellationToken)
    {
        int hr = devices.Item(endpointIndex, out IMMDevice device);
        if (!CoreAudio.Succeeded(hr) || device is null)
        {
            return false;
        }

        try
        {
            if (!TryReadEndpointIdentity(device, out string endpointId))
            {
                return false;
            }

            string endpointName = SanitizeName(
                ReadFriendlyName(device),
                MaximumEndpointNameLength,
                "Playback device");
            Guid managerId = CoreAudio.AudioSessionManager2Id;
            hr = device.Activate(
                ref managerId,
                CoreAudio.ClassContextAll,
                IntPtr.Zero,
                out object managerObject);
            if (!CoreAudio.Succeeded(hr) || managerObject is not IAudioSessionManager2 manager)
            {
                CoreAudio.ReleaseComObject(managerObject);
                return false;
            }

            try
            {
                hr = manager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
                if (!CoreAudio.Succeeded(hr) || sessionEnumerator is null)
                {
                    return false;
                }

                try
                {
                    hr = sessionEnumerator.GetCount(out int count);
                    if (!CoreAudio.Succeeded(hr) || count < 0)
                    {
                        return false;
                    }

                    int availableObservationSlots = Math.Max(0, MaximumObservedSessionCount - observed);
                    int boundedCount = Math.Min(count, availableObservationSlots);
                    omitted += Math.Max(0, count - boundedCount);
                    for (int sessionIndex = 0; sessionIndex < boundedCount; sessionIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        observed++;
                        if (!TryReadSession(
                                sessionEnumerator,
                                sessionIndex,
                                endpointId,
                                endpointName,
                                out AudioSession? session))
                        {
                            inaccessible++;
                            continue;
                        }

                        if (session is null)
                        {
                            continue;
                        }

                        if (sessions.Count >= MaximumSessionCount)
                        {
                            omitted++;
                            continue;
                        }

                        sessions.Add(session);
                    }
                }
                finally
                {
                    CoreAudio.ReleaseComObject(sessionEnumerator);
                }
            }
            finally
            {
                CoreAudio.ReleaseComObject(manager);
            }

            return true;
        }
        finally
        {
            CoreAudio.ReleaseComObject(device);
        }
    }

    private static bool TryReadSession(
        IAudioSessionEnumerator sessionEnumerator,
        int sessionIndex,
        string endpointId,
        string endpointName,
        out AudioSession? session)
    {
        session = null;
        int hr = sessionEnumerator.GetSession(sessionIndex, out IAudioSessionControl sessionControl);
        if (!CoreAudio.Succeeded(hr) || sessionControl is null)
        {
            return false;
        }

        try
        {
            if (sessionControl is not IAudioSessionControl2 control2 ||
                control2.GetState(out CoreAudioSessionState state) != CoreAudio.Ok)
            {
                return false;
            }

            if (state != CoreAudioSessionState.Active)
            {
                return true;
            }

            if (sessionControl is not ISimpleAudioVolume volume ||
                volume.GetMasterVolume(out float scalar) != CoreAudio.Ok ||
                volume.GetMute(out bool muted) != CoreAudio.Ok)
            {
                return false;
            }

            bool systemSounds = control2.IsSystemSoundsSession() == CoreAudio.Ok;
            int processResult = control2.GetProcessId(out uint processId);
            bool singleProcess = CoreAudio.Succeeded(processResult) && processId != 0;
            string? displayName = TryGetDisplayName(control2);
            ProcessObservation process = ObserveProcess(processId, singleProcess);
            string name = systemSounds
                ? "System sounds"
                : ChooseSessionName(displayName, process.Name);

            AudioSessionIdentity? identity = null;
            bool canControl = false;
            string availability;
            if (systemSounds)
            {
                availability = "Windows system-sounds session";
            }
            else if (!singleProcess)
            {
                availability = "Multi-process or transferred session";
            }
            else if (!process.IsCurrent)
            {
                availability = "Owning process is no longer current";
            }
            else if (!TryGetSessionInstanceIdentifier(control2, out string instanceId))
            {
                availability = "Session identity unavailable";
            }
            else
            {
                canControl = true;
                availability = "Volume and mute available";
                identity = new AudioSessionIdentity(
                    HashIdentity(endpointId),
                    HashIdentity(instanceId),
                    processId,
                    process.StartedUtcTicks);
            }

            session = new AudioSession(
                name,
                endpointName,
                Math.Clamp(scalar, 0f, 1f),
                muted,
                canControl,
                availability,
                identity);
            return true;
        }
        finally
        {
            CoreAudio.ReleaseComObject(sessionControl);
        }
    }

    internal static AudioObservationState ClassifyState(
        bool anyEndpointEnumerationSucceeded,
        int inaccessibleSessionCount,
        int omittedSessionCount)
    {
        if (!anyEndpointEnumerationSucceeded)
        {
            return AudioObservationState.Unavailable;
        }

        return inaccessibleSessionCount > 0 || omittedSessionCount > 0
            ? AudioObservationState.Partial
            : AudioObservationState.Current;
    }

    internal static string SanitizeName(string? value, int maximumLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string safe = new(value
            .Where(character => !char.IsControl(character))
            .Take(Math.Max(1, maximumLength))
            .ToArray());
        safe = string.Join(
            " ",
            safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return safe.Length == 0 || LooksPathLike(safe) ? fallback : safe;
    }

    internal static bool LooksPathLike(string value)
    {
        if (value.StartsWith("\\\\", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int index = 0; index + 2 < value.Length; index++)
        {
            if (char.IsAsciiLetter(value[index]) &&
                value[index + 1] == ':' &&
                value[index + 2] is '\\' or '/')
            {
                return true;
            }
        }

        int environmentEnd = value.IndexOf("%\\", StringComparison.Ordinal);
        return value.StartsWith('%') && environmentEnd > 1;
    }

    internal static bool IsReadBackMatch(
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute,
        double? observedVolumeScalar,
        bool? observedMute) => kind switch
    {
        AudioSessionMutationKind.Volume =>
            requestedVolumeScalar is double requested &&
            observedVolumeScalar is double observed &&
            Math.Abs(requested - observed) <= 0.005,
        AudioSessionMutationKind.Mute =>
            requestedMute is bool requested &&
            observedMute == requested,
        _ => false
    };

    internal static string HashIdentity(string value)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest);
    }

    private static AudioSessionSnapshot BuildSnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan captureDuration,
        AudioObservationState state,
        List<AudioSession> sessions,
        int observed,
        int inaccessible,
        int omitted,
        string provenance,
        List<string> limitations) =>
        new(
            capturedAtUtc,
            captureDuration,
            state,
            sessions
                .OrderByDescending(session => session.CanControl)
                .ThenBy(session => session.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(session => session.EndpointName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            observed,
            inaccessible,
            omitted,
            provenance,
            limitations);

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

    private static bool TryReadEndpointIdentity(IMMDevice device, out string id)
    {
        int hr = device.GetId(out string value);
        id = CoreAudio.Succeeded(hr) ? value : string.Empty;
        return id.Length > 0;
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        int hr = device.OpenPropertyStore(CoreAudio.StorageRead, out IPropertyStore store);
        if (!CoreAudio.Succeeded(hr) || store is null)
        {
            return null;
        }

        try
        {
            PropertyKey key = CoreAudio.FriendlyNameKey;
            hr = store.GetValue(ref key, out PropVariant value);
            if (!CoreAudio.Succeeded(hr))
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
            CoreAudio.ReleaseComObject(store);
        }
    }

    private static string? TryGetDisplayName(IAudioSessionControl2 control)
    {
        int hr = control.GetDisplayName(out string value);
        return CoreAudio.Succeeded(hr) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool TryGetSessionInstanceIdentifier(
        IAudioSessionControl2 control,
        out string value)
    {
        int hr = control.GetSessionInstanceIdentifier(out string identifier);
        value = CoreAudio.Succeeded(hr) ? identifier : string.Empty;
        return value.Length > 0;
    }

    private static string ChooseSessionName(string? displayName, string? processName)
    {
        foreach (string? candidate in new[] { displayName, processName })
        {
            string safe = SanitizeName(candidate, MaximumSessionNameLength, string.Empty);
            if (safe.Length > 0)
            {
                return safe;
            }
        }

        return "Audio session";
    }

    private static ProcessObservation ObserveProcess(uint processId, bool singleProcess)
    {
        if (!singleProcess || processId > int.MaxValue)
        {
            return new ProcessObservation(null, 0, false);
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            long startedUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            string? processName = process.ProcessName;
            bool current = !process.HasExited;
            return new ProcessObservation(processName, startedUtcTicks, current);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new ProcessObservation(null, 0, false);
        }
    }

    private sealed record ProcessObservation(string? Name, long StartedUtcTicks, bool IsCurrent);
}
