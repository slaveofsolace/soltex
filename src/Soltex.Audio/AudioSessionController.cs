using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Soltex.Audio;

/// <summary>
/// Guarded per-session volume and mute changes. A request is admitted only for
/// an identity returned by the current process, then the Core Audio endpoint,
/// session instance, process ID, process start time, active state, and
/// system-sounds boundary are revalidated immediately before the write.
/// </summary>
public static class AudioSessionController
{
    public static Task<AudioSessionMutationResult> SetVolumeAsync(
        AudioSession session,
        double volumePercent,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            session,
            AudioSessionMutationKind.Volume,
            volumePercent,
            requestedMute: null,
            new CoreAudioSessionMutationBackend(),
            cancellationToken);

    public static Task<AudioSessionMutationResult> SetMuteAsync(
        AudioSession session,
        bool muted,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            session,
            AudioSessionMutationKind.Mute,
            requestedVolumePercent: null,
            muted,
            new CoreAudioSessionMutationBackend(),
            cancellationToken);

    internal static async Task<AudioSessionMutationResult> ExecuteAsync(
        AudioSession session,
        AudioSessionMutationKind kind,
        double? requestedVolumePercent,
        bool? requestedMute,
        IAudioSessionMutationBackend backend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(backend);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeRequest(
                session,
                kind,
                requestedVolumePercent,
                requestedMute,
                out double? requestedVolumeScalar,
                out string rejection))
        {
            return BuildResult(
                session,
                kind,
                AudioSessionMutationStatus.Rejected,
                requestedVolumePercent,
                requestedMute,
                observedVolumeScalar: null,
                observedMute: null,
                rejection);
        }

        AudioSessionMutationBackendResult backendResult = await Task.Run(
            () => backend.Apply(
                session.Identity!,
                kind,
                requestedVolumeScalar,
                requestedMute,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        AudioSessionMutationStatus status = backendResult.Status;
        if (status == AudioSessionMutationStatus.Applied &&
            !AudioSessionProvider.IsReadBackMatch(
                kind,
                requestedVolumeScalar,
                requestedMute,
                backendResult.ObservedVolumeScalar,
                backendResult.ObservedMute))
        {
            status = AudioSessionMutationStatus.ReadBackMismatch;
        }

        return BuildResult(
            session,
            kind,
            status,
            requestedVolumePercent,
            requestedMute,
            backendResult.ObservedVolumeScalar,
            backendResult.ObservedMute,
            detail: null);
    }

    internal static bool TryNormalizeRequest(
        AudioSession session,
        AudioSessionMutationKind kind,
        double? requestedVolumePercent,
        bool? requestedMute,
        out double? requestedVolumeScalar,
        out string rejection)
    {
        requestedVolumeScalar = null;
        rejection = string.Empty;
        if (!session.CanControl || session.Identity is null)
        {
            rejection = $"{session.Name} does not expose a safely controllable single-process session.";
            return false;
        }

        if (kind == AudioSessionMutationKind.Volume)
        {
            if (requestedVolumePercent is not double percent ||
                !double.IsFinite(percent) ||
                percent is < 0 or > 100)
            {
                rejection = "Volume must be between 0 and 100 percent.";
                return false;
            }

            requestedVolumeScalar = percent / 100;
            return true;
        }

        if (kind == AudioSessionMutationKind.Mute && requestedMute is bool)
        {
            return true;
        }

        rejection = "The requested audio-session change is incomplete.";
        return false;
    }

    private static AudioSessionMutationResult BuildResult(
        AudioSession session,
        AudioSessionMutationKind kind,
        AudioSessionMutationStatus status,
        double? requestedVolumePercent,
        bool? requestedMute,
        double? observedVolumeScalar,
        bool? observedMute,
        string? detail)
    {
        double? observedPercent = observedVolumeScalar is double scalar
            ? Math.Clamp(scalar * 100, 0, 100)
            : null;
        string message = detail ?? status switch
        {
            AudioSessionMutationStatus.Applied when kind == AudioSessionMutationKind.Volume =>
                $"{session.Name} volume is {observedPercent ?? requestedVolumePercent ?? 0:F0}% (verified).",
            AudioSessionMutationStatus.Applied when observedMute == true =>
                $"{session.Name} is muted (verified).",
            AudioSessionMutationStatus.Applied =>
                $"{session.Name} is unmuted (verified).",
            AudioSessionMutationStatus.TargetChanged =>
                $"{session.Name} changed or ended before the request; nothing was changed.",
            AudioSessionMutationStatus.ReadBackMismatch =>
                $"Windows did not confirm the requested {DescribeKind(kind)} for {session.Name}.",
            AudioSessionMutationStatus.Unavailable =>
                $"Windows could not apply the requested {DescribeKind(kind)} for {session.Name}.",
            _ =>
                $"The requested {DescribeKind(kind)} for {session.Name} was rejected."
        };

        return new AudioSessionMutationResult(
            kind,
            status,
            session.Name,
            requestedVolumePercent,
            requestedMute,
            observedPercent,
            observedMute,
            message);
    }

    private static string DescribeKind(AudioSessionMutationKind kind) =>
        kind == AudioSessionMutationKind.Volume ? "volume" : "mute state";
}

internal interface IAudioSessionMutationBackend
{
    AudioSessionMutationBackendResult Apply(
        AudioSessionIdentity identity,
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute,
        CancellationToken cancellationToken);
}

internal sealed class CoreAudioSessionMutationBackend : IAudioSessionMutationBackend
{
    public AudioSessionMutationBackendResult Apply(
        AudioSessionIdentity identity,
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute,
        CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? deviceEnumerator = CreateEnumerator();
        if (deviceEnumerator is null)
        {
            return Unavailable();
        }

        try
        {
            int hr = deviceEnumerator.EnumAudioEndpoints(
                DataFlow.Render,
                DeviceStateMask.Active,
                out IMMDeviceCollection devices);
            if (!CoreAudio.Succeeded(hr) || devices is null)
            {
                return Unavailable();
            }

            try
            {
                hr = devices.GetCount(out uint endpointCount);
                if (!CoreAudio.Succeeded(hr))
                {
                    return Unavailable();
                }

                uint boundedEndpointCount = Math.Min(
                    endpointCount,
                    (uint)AudioEndpointProvider.MaximumEndpointCount);
                for (uint endpointIndex = 0; endpointIndex < boundedEndpointCount; endpointIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AudioSessionMutationBackendResult? result = TryApplyToEndpoint(
                        devices,
                        endpointIndex,
                        identity,
                        kind,
                        requestedVolumeScalar,
                        requestedMute,
                        cancellationToken);
                    if (result is not null)
                    {
                        return result;
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

        return Changed();
    }

    private static AudioSessionMutationBackendResult? TryApplyToEndpoint(
        IMMDeviceCollection devices,
        uint endpointIndex,
        AudioSessionIdentity identity,
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute,
        CancellationToken cancellationToken)
    {
        int hr = devices.Item(endpointIndex, out IMMDevice device);
        if (!CoreAudio.Succeeded(hr) || device is null)
        {
            return null;
        }

        try
        {
            hr = device.GetId(out string endpointId);
            if (!CoreAudio.Succeeded(hr) ||
                !string.Equals(
                    AudioSessionProvider.HashIdentity(endpointId),
                    identity.EndpointIdentityHash,
                    StringComparison.Ordinal))
            {
                return null;
            }

            Guid managerId = CoreAudio.AudioSessionManager2Id;
            hr = device.Activate(
                ref managerId,
                CoreAudio.ClassContextAll,
                IntPtr.Zero,
                out object managerObject);
            if (!CoreAudio.Succeeded(hr) || managerObject is not IAudioSessionManager2 manager)
            {
                CoreAudio.ReleaseComObject(managerObject);
                return Unavailable();
            }

            try
            {
                hr = manager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
                if (!CoreAudio.Succeeded(hr) || sessionEnumerator is null)
                {
                    return Unavailable();
                }

                try
                {
                    hr = sessionEnumerator.GetCount(out int count);
                    if (!CoreAudio.Succeeded(hr) || count < 0)
                    {
                        return Unavailable();
                    }

                    int boundedCount = Math.Min(count, AudioSessionProvider.MaximumObservedSessionCount);
                    for (int sessionIndex = 0; sessionIndex < boundedCount; sessionIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AudioSessionMutationBackendResult? result = TryApplyToSession(
                            sessionEnumerator,
                            sessionIndex,
                            identity,
                            kind,
                            requestedVolumeScalar,
                            requestedMute);
                        if (result is not null)
                        {
                            return result;
                        }
                    }

                    return Changed();
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
        }
        finally
        {
            CoreAudio.ReleaseComObject(device);
        }
    }

    private static AudioSessionMutationBackendResult? TryApplyToSession(
        IAudioSessionEnumerator sessionEnumerator,
        int sessionIndex,
        AudioSessionIdentity identity,
        AudioSessionMutationKind kind,
        double? requestedVolumeScalar,
        bool? requestedMute)
    {
        int hr = sessionEnumerator.GetSession(sessionIndex, out IAudioSessionControl sessionControl);
        if (!CoreAudio.Succeeded(hr) || sessionControl is null)
        {
            return null;
        }

        try
        {
            if (sessionControl is not IAudioSessionControl2 control2 ||
                control2.GetSessionInstanceIdentifier(out string instanceId) < 0 ||
                !string.Equals(
                    AudioSessionProvider.HashIdentity(instanceId),
                    identity.SessionInstanceIdentityHash,
                    StringComparison.Ordinal))
            {
                return null;
            }

            if (control2.IsSystemSoundsSession() == CoreAudio.Ok ||
                control2.GetState(out CoreAudioSessionState state) != CoreAudio.Ok ||
                state != CoreAudioSessionState.Active ||
                control2.GetProcessId(out uint processId) < 0 ||
                processId != identity.ProcessId ||
                !ProcessIdentityMatches(processId, identity.ProcessStartedUtcTicks))
            {
                return Changed();
            }

            if (sessionControl is not ISimpleAudioVolume volume)
            {
                return Unavailable();
            }

            Guid eventContext = Guid.NewGuid();
            hr = kind switch
            {
                AudioSessionMutationKind.Volume when requestedVolumeScalar is double scalar =>
                    volume.SetMasterVolume((float)scalar, ref eventContext),
                AudioSessionMutationKind.Mute when requestedMute is bool muted =>
                    volume.SetMute(muted, ref eventContext),
                _ => unchecked((int)0x80070057)
            };
            if (!CoreAudio.Succeeded(hr))
            {
                return Unavailable();
            }

            int volumeResult = volume.GetMasterVolume(out float observedVolume);
            int muteResult = volume.GetMute(out bool observedMute);
            if (!CoreAudio.Succeeded(volumeResult) || !CoreAudio.Succeeded(muteResult))
            {
                return new AudioSessionMutationBackendResult(
                    AudioSessionMutationStatus.ReadBackMismatch,
                    null,
                    null);
            }

            return new AudioSessionMutationBackendResult(
                AudioSessionMutationStatus.Applied,
                Math.Clamp(observedVolume, 0f, 1f),
                observedMute);
        }
        finally
        {
            CoreAudio.ReleaseComObject(sessionControl);
        }
    }

    private static bool ProcessIdentityMatches(uint processId, long startedUtcTicks)
    {
        if (processId == 0 || processId > int.MaxValue || startedUtcTicks <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return !process.HasExited &&
                process.StartTime.ToUniversalTime().Ticks == startedUtcTicks;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
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

    private static AudioSessionMutationBackendResult Unavailable() =>
        new(AudioSessionMutationStatus.Unavailable, null, null);

    private static AudioSessionMutationBackendResult Changed() =>
        new(AudioSessionMutationStatus.TargetChanged, null, null);
}
