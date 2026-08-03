namespace WaveSlate.Security;

public enum ContentVerdict
{
    Clean,
    Allowed,
    ReviewRecommended,
    BlockedByPolicy,
    Malware,
    Unavailable,
    Error
}

public enum WindowsSecurityHealth
{
    Good,
    NotMonitored,
    Poor,
    Snoozed,
    Unknown
}

public enum SecurityEventSeverity
{
    Information,
    Warning,
    Critical
}

public enum ProtectionMonitorState
{
    Current,
    ProviderManaged,
    Degraded,
    Recovered
}

public enum DefenderEventKind
{
    ScanStarted,
    ScanCompleted,
    ScanCancelled,
    Detection,
    Remediation,
    RemediationFailed,
    ProtectionEnabled,
    ProtectionDisabled,
    ProtectionFailure,
    ProtectionRecovered,
    ConfigurationChanged,
    TamperBlocked,
    Other
}

public sealed record ContentScanResult(
    ContentVerdict Verdict,
    string Engine,
    uint NativeResult,
    string Detail)
{
    public bool ShouldBlock => Verdict is ContentVerdict.Malware or ContentVerdict.BlockedByPolicy;
}

public sealed record DefenderHealthSnapshot(
    DateTimeOffset CheckedAtUtc,
    WindowsSecurityHealth WindowsSecurityCenterHealth,
    bool StatusQuerySucceeded,
    string? AMRunningMode,
    bool AMServiceEnabled,
    bool AntivirusEnabled,
    bool RealTimeProtectionEnabled,
    bool BehaviorMonitorEnabled,
    bool IoavProtectionEnabled,
    bool NetworkInspectionEnabled,
    bool TamperProtected,
    bool CloudProtectionEnabled,
    bool SignaturesOutOfDate,
    string? AntivirusSignatureVersion,
    DateTimeOffset? AntivirusSignatureUpdatedAt,
    uint? QuickScanAgeDays,
    uint? FullScanAgeDays,
    string? Error)
{
    public bool ObservationSucceeded =>
        WindowsSecurityCenterHealth != WindowsSecurityHealth.Unknown || StatusQuerySucceeded;

    public bool DefenderIsPrimary => string.Equals(AMRunningMode, "Normal", StringComparison.OrdinalIgnoreCase);

    public bool IsProtected => WindowsSecurityCenterHealth switch
    {
        WindowsSecurityHealth.Good => true,
        WindowsSecurityHealth.Poor or
        WindowsSecurityHealth.Snoozed or
        WindowsSecurityHealth.NotMonitored => false,
        _ => StatusQuerySucceeded &&
             DefenderIsPrimary &&
             AntivirusEnabled &&
             RealTimeProtectionEnabled
    };

    public string Summary => WindowsSecurityCenterHealth switch
    {
        WindowsSecurityHealth.Good => "Windows antivirus protection is active",
        WindowsSecurityHealth.Snoozed => "Windows antivirus protection is snoozed",
        WindowsSecurityHealth.Poor => "Windows antivirus protection needs attention",
        WindowsSecurityHealth.NotMonitored => "Windows Security is not monitoring an antivirus provider",
        _ when IsProtected => "Microsoft Defender reports active protection; Windows Security Center health is unavailable",
        _ => "Windows antivirus protection could not be confirmed"
    };
}

public sealed record ProtectionMonitorUpdate(
    DefenderHealthSnapshot Observed,
    DefenderHealthSnapshot? LastKnownGood,
    ProtectionMonitorState State,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessfulCheckAtUtc,
    TimeSpan QueryDuration,
    TimeSpan NextRefreshIn,
    string Detail);

public sealed record DefenderOperationalEvent(
    long RecordId,
    int EventId,
    DateTimeOffset TimestampUtc,
    DefenderEventKind Kind,
    SecurityEventSeverity Severity,
    string Title,
    string Detail,
    bool ResourcePathRedacted);

public sealed record DefenderEventQueryResult(
    DateTimeOffset CheckedAtUtc,
    bool Succeeded,
    IReadOnlyList<DefenderOperationalEvent> Events,
    TimeSpan Duration,
    string? Error);

public sealed record DefenderCommandResult(
    bool Succeeded,
    string Operation,
    string Message,
    int ExitCode,
    TimeSpan Duration);

public sealed record FileAssessment(
    string Path,
    string Sha256,
    long Length,
    ContentVerdict Verdict,
    string Engine,
    string Detail,
    DateTimeOffset AssessedAtUtc)
{
    public bool ShouldBlock => Verdict is ContentVerdict.Malware or ContentVerdict.BlockedByPolicy;
}

public sealed record QuarantineEntry(
    Guid Id,
    string OriginalPath,
    string StoredFileName,
    string Sha256,
    long OriginalLength,
    string Detection,
    DateTimeOffset QuarantinedAtUtc);

public sealed record AllowListEntry(
    string Sha256,
    string DisplayName,
    string Reason,
    DateTimeOffset AddedAtUtc);

public sealed record SecurityAuditEvent(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string EventType,
    SecurityEventSeverity Severity,
    string Message,
    string? PathHash,
    string? Detail,
    string PreviousHash,
    string EntryHash);
