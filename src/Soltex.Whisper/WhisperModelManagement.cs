namespace Soltex.Whisper;

/// <summary>
/// Stable identities for the first local transcription configuration. These values
/// are settings identifiers only; download locations and filesystem paths remain in
/// the Windows adapter.
/// </summary>
public static class WhisperLocalModelDefaults
{
    public const string ProviderId = "local-whisper";
    public const string ModelId = "large-v3-turbo-q5_0";
    public const string RuntimeId = "cpu";
}

public enum WhisperModelInstallState
{
    NotInstalled,
    Installing,
    Ready,
    Invalid,
    Faulted
}

public enum WhisperModelFailureKind
{
    None,
    Busy,
    Cancelled,
    Network,
    ResponseRejected,
    SizeMismatch,
    DigestMismatch,
    StorageUnavailable,
    AccessDenied,
    OwnershipChanged,
    Unknown
}

/// <summary>
/// Content-free model state suitable for readiness and setup surfaces. It never
/// exposes a local path, download URL, response body, or model bytes.
/// </summary>
public sealed record WhisperModelStatus(
    string ProviderId,
    string ModelId,
    string RuntimeId,
    WhisperModelInstallState State,
    long ExpectedBytes,
    long InstalledBytes,
    WhisperModelFailureKind FailureKind,
    string? FailureReason)
{
    public bool IsInstalled => State == WhisperModelInstallState.Ready;

    public bool IsVerified => State == WhisperModelInstallState.Ready;
}

/// <summary>
/// Content-free byte progress for one explicit install or repair operation.
/// </summary>
public sealed record WhisperModelInstallProgress(long ReceivedBytes, long ExpectedBytes)
{
    public double Fraction => ExpectedBytes <= 0
        ? 0
        : Math.Clamp((double)ReceivedBytes / ExpectedBytes, 0, 1);
}

public sealed class WhisperModelInstallException : Exception
{
    public WhisperModelInstallException(
        WhisperModelFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (kind == WhisperModelFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "A model installation failure must identify its category.");
        }

        Kind = kind;
    }

    public WhisperModelFailureKind Kind { get; }
}

/// <summary>
/// Management boundary for local model state. Implementations own all paths and
/// network details; callers receive only content-free state and progress.
/// </summary>
public interface IWhisperModelManager : IAsyncDisposable
{
    ValueTask<WhisperModelStatus> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<WhisperModelStatus> InstallAsync(
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken);

    ValueTask<WhisperModelStatus> RepairAsync(
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken);

    ValueTask DeleteAsync(CancellationToken cancellationToken);
}
