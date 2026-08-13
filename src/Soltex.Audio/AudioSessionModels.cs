using System.Collections.ObjectModel;

namespace Soltex.Audio;

/// <summary>The two shared-mode session changes Soltex currently supports.</summary>
public enum AudioSessionMutationKind
{
    Volume,
    Mute
}

/// <summary>Truthful outcome of a requested Windows Core Audio session change.</summary>
public enum AudioSessionMutationStatus
{
    Applied,
    Rejected,
    TargetChanged,
    Unavailable,
    ReadBackMismatch
}

/// <summary>
/// One active shared-mode render session. Raw endpoint and session identifiers
/// are never exposed or persisted; an internal one-way identity is retained
/// only long enough to revalidate an explicit control request.
/// </summary>
public sealed class AudioSession
{
    internal AudioSession(
        string name,
        string endpointName,
        double volumeScalar,
        bool isMuted,
        bool canControl,
        string controlAvailability,
        AudioSessionIdentity? identity)
    {
        Name = name;
        EndpointName = endpointName;
        VolumeScalar = Math.Clamp(volumeScalar, 0, 1);
        IsMuted = isMuted;
        CanControl = canControl;
        ControlAvailability = controlAvailability;
        Identity = identity;
    }

    public string Name { get; }

    public string EndpointName { get; }

    public double VolumeScalar { get; }

    public double VolumePercent => VolumeScalar * 100;

    public bool IsMuted { get; }

    public bool CanControl { get; }

    public string ControlAvailability { get; }

    internal AudioSessionIdentity? Identity { get; }
}

public sealed class AudioSessionSnapshot
{
    internal AudioSessionSnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan captureDuration,
        AudioObservationState state,
        IReadOnlyList<AudioSession> sessions,
        int observedSessionCount,
        int inaccessibleSessionCount,
        int omittedSessionCount,
        string provenance,
        IReadOnlyList<string> limitations)
    {
        CapturedAtUtc = capturedAtUtc;
        CaptureDuration = captureDuration;
        State = state;
        Sessions = new ReadOnlyCollection<AudioSession>(sessions.ToArray());
        ObservedSessionCount = Math.Max(0, observedSessionCount);
        InaccessibleSessionCount = Math.Max(0, inaccessibleSessionCount);
        OmittedSessionCount = Math.Max(0, omittedSessionCount);
        Provenance = provenance;
        Limitations = new ReadOnlyCollection<string>(limitations.ToArray());
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public TimeSpan CaptureDuration { get; }

    public AudioObservationState State { get; }

    public IReadOnlyList<AudioSession> Sessions { get; }

    /// <summary>Number of session slots inspected before active-state filtering.</summary>
    public int ObservedSessionCount { get; }

    public int InaccessibleSessionCount { get; }

    public int OmittedSessionCount { get; }

    public string Provenance { get; }

    public IReadOnlyList<string> Limitations { get; }
}

public sealed record AudioSessionMutationResult(
    AudioSessionMutationKind Kind,
    AudioSessionMutationStatus Status,
    string SessionName,
    double? RequestedVolumePercent,
    bool? RequestedMute,
    double? ObservedVolumePercent,
    bool? ObservedMute,
    string Message)
{
    public bool Succeeded => Status == AudioSessionMutationStatus.Applied;
}

internal sealed record AudioSessionIdentity(
    string EndpointIdentityHash,
    string SessionInstanceIdentityHash,
    uint ProcessId,
    long ProcessStartedUtcTicks);

internal sealed record AudioSessionMutationBackendResult(
    AudioSessionMutationStatus Status,
    double? ObservedVolumeScalar,
    bool? ObservedMute);
