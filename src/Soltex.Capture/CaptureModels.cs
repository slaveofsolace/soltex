using System.Collections.ObjectModel;

namespace Soltex.Capture;

public enum CaptureSourceKind
{
    Display,
    Window,
    Region
}

public enum CaptureAudioMode
{
    None,
    Microphone,
    System,
    MicrophoneAndSystem
}

public enum CaptureEncoderPreference
{
    HardwarePreferred,
    SoftwareOnly
}

public enum CaptureOutputKind
{
    Screenshot,
    Recording,
    Replay
}

public enum CaptureSessionPhase
{
    Idle,
    Selecting,
    Ready,
    Recording,
    Paused,
    Stopping,
    Completed,
    Faulted
}

public enum CaptureFailureKind
{
    None,
    Unsupported,
    ConsentDeclined,
    SourceClosed,
    ProtectedContent,
    EncoderUnavailable,
    DeviceLost,
    StorageUnavailable,
    StorageExhausted,
    Cancelled,
    Unexpected
}

public sealed record CaptureRegion(int X, int Y, int Width, int Height)
{
    public const int MaximumDimension = 16_384;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public CaptureRegion Normalize()
    {
        int width = Math.Clamp(Width, 0, MaximumDimension);
        int height = Math.Clamp(Height, 0, MaximumDimension);
        return new CaptureRegion(
            Math.Clamp(X, -MaximumDimension, MaximumDimension),
            Math.Clamp(Y, -MaximumDimension, MaximumDimension),
            width,
            height);
    }
}

public sealed record ReplayBufferPolicy(bool Enabled, int DurationSeconds, long MaximumBytes)
{
    public const int DefaultDurationSeconds = 30;
    public const int MinimumDurationSeconds = 5;
    public const int MaximumDurationSeconds = 120;
    public const long DefaultMaximumBytes = 512L * 1_024 * 1_024;
    public const long MinimumMaximumBytes = 16L * 1_024 * 1_024;
    public const long AbsoluteMaximumBytes = 1L * 1_024 * 1_024 * 1_024;

    public static ReplayBufferPolicy Disabled { get; } =
        new(false, DefaultDurationSeconds, DefaultMaximumBytes);

    public ReplayBufferPolicy Normalize() =>
        new(
            Enabled,
            Math.Clamp(DurationSeconds, MinimumDurationSeconds, MaximumDurationSeconds),
            Math.Clamp(MaximumBytes, MinimumMaximumBytes, AbsoluteMaximumBytes));
}

public sealed record CaptureProfile(
    int ContractVersion,
    CaptureSourceKind SourceKind,
    CaptureAudioMode AudioMode,
    CaptureEncoderPreference EncoderPreference,
    int FrameRate,
    int VideoBitrate,
    bool IncludePointer,
    ReplayBufferPolicy Replay)
{
    public const int CurrentContractVersion = 1;
    public const int DefaultFrameRate = 60;
    public const int MinimumFrameRate = 15;
    public const int MaximumFrameRate = 60;
    public const int DefaultVideoBitrate = 12_000_000;
    public const int MinimumVideoBitrate = 1_000_000;
    public const int MaximumVideoBitrate = 50_000_000;

    public static CaptureProfile Default { get; } =
        new(
            CurrentContractVersion,
            CaptureSourceKind.Display,
            CaptureAudioMode.None,
            CaptureEncoderPreference.HardwarePreferred,
            DefaultFrameRate,
            DefaultVideoBitrate,
            IncludePointer: true,
            ReplayBufferPolicy.Disabled);

    public CaptureProfile Normalize()
    {
        CaptureSourceKind source = Enum.IsDefined(SourceKind)
            ? SourceKind
            : CaptureSourceKind.Display;
        CaptureAudioMode audio = Enum.IsDefined(AudioMode)
            ? AudioMode
            : CaptureAudioMode.None;
        CaptureEncoderPreference encoder = Enum.IsDefined(EncoderPreference)
            ? EncoderPreference
            : CaptureEncoderPreference.HardwarePreferred;

        return new CaptureProfile(
            CurrentContractVersion,
            source,
            audio,
            encoder,
            Math.Clamp(FrameRate, MinimumFrameRate, MaximumFrameRate),
            Math.Clamp(VideoBitrate, MinimumVideoBitrate, MaximumVideoBitrate),
            IncludePointer,
            (Replay ?? ReplayBufferPolicy.Disabled).Normalize());
    }
}

public sealed record CaptureCapability(
    string Id,
    bool Available,
    bool RequiresConsent,
    string Detail);

public sealed class CaptureCapabilitySnapshot
{
    public CaptureCapabilitySnapshot(
        DateTimeOffset capturedAtUtc,
        IEnumerable<CaptureCapability> capabilities,
        string? limitation = null)
    {
        CapturedAtUtc = capturedAtUtc;
        Capabilities = new ReadOnlyCollection<CaptureCapability>(
            capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability.Id))
                .Take(16)
                .ToArray());
        Limitation = NormalizeText(limitation, 240);
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public IReadOnlyList<CaptureCapability> Capabilities { get; }

    public string Limitation { get; }

    public bool IsAvailable(string id) =>
        Capabilities.Any(capability =>
            capability.Available &&
            string.Equals(capability.Id, id, StringComparison.Ordinal));

    internal static string NormalizeText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}

public sealed record ClipManifest(
    Guid ClipId,
    CaptureOutputKind Kind,
    DateTimeOffset CreatedAtUtc,
    TimeSpan Duration,
    int Width,
    int Height,
    int FrameRate,
    CaptureAudioMode AudioMode,
    string Encoder,
    long FileBytes,
    string Sha256,
    string FileName)
{
    public const int MaximumFileNameLength = 128;

    public ClipManifest Normalize()
    {
        Guid id = ClipId == Guid.Empty ? Guid.NewGuid() : ClipId;
        CaptureOutputKind kind = Enum.IsDefined(Kind) ? Kind : CaptureOutputKind.Screenshot;
        CaptureAudioMode audio = Enum.IsDefined(AudioMode) ? AudioMode : CaptureAudioMode.None;
        string encoder = CaptureCapabilitySnapshot.NormalizeText(Encoder, 64);
        string hash = NormalizeSha256(Sha256);
        string fileName = CaptureStoragePolicy.NormalizeFileName(FileName, kind);
        TimeSpan duration = Duration < TimeSpan.Zero
            ? TimeSpan.Zero
            : Duration > TimeSpan.FromHours(24)
                ? TimeSpan.FromHours(24)
                : Duration;

        return new ClipManifest(
            id,
            kind,
            CreatedAtUtc == default ? DateTimeOffset.UtcNow : CreatedAtUtc,
            duration,
            Math.Clamp(Width, 0, CaptureRegion.MaximumDimension),
            Math.Clamp(Height, 0, CaptureRegion.MaximumDimension),
            Math.Clamp(FrameRate, 0, CaptureProfile.MaximumFrameRate),
            audio,
            encoder,
            Math.Clamp(FileBytes, 0, CaptureStoragePolicy.MaximumClipBytes),
            hash,
            fileName);
    }

    private static string NormalizeSha256(string? value)
    {
        string candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return candidate.Length == 64 && candidate.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
                ? candidate
                : string.Empty;
    }
}
