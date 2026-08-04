using WaveSlate.Security;

namespace WaveSlate.Update;

public enum UpdateTrustStatus
{
    Active,
    Retiring,
    Retired,
    Revoked,
    Compromised
}

public sealed record UpdateRsaTrustKey(
    string KeyId,
    string PublicKeyPem,
    UpdateTrustStatus Status,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    DateTimeOffset? RetireAfterUtc);

public sealed record UpdateTlsPin(
    string Origin,
    string SpkiSha256,
    UpdateTrustStatus Status,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    DateTimeOffset? RetireAfterUtc);

public sealed record UpdatePublisherPin(
    ApprovedPublisherIdentity Identity,
    UpdateTrustStatus Status,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    DateTimeOffset? RetireAfterUtc);

public sealed record UpdateTrustPolicy(
    int SchemaVersion,
    long Sequence,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    int DescriptorSignatureQuorum,
    List<UpdateRsaTrustKey> MetadataKeys,
    List<UpdateRsaTrustKey> ReleaseKeys,
    List<UpdateTlsPin> TlsPins,
    List<UpdatePublisherPin> Publishers);

public sealed record UpdateDetachedSignature(string KeyId, string SignatureBase64);

public sealed record UpdateArtifactDescriptor(
    string Name,
    string Uri,
    long Length,
    string Sha256);

public sealed record UpdateAcquisitionDescriptor(
    int SchemaVersion,
    string Product,
    string DescriptorId,
    string Channel,
    long Sequence,
    string Version,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ReleaseKeyId,
    string Origin,
    List<string> AllowedRedirectOrigins,
    int MaximumRedirects,
    int HeaderTimeoutSeconds,
    int ReadTimeoutSeconds,
    int OverallTimeoutSeconds,
    List<UpdateArtifactDescriptor> Artifacts);

public enum UpdateDescriptorStatus
{
    Verified,
    InvalidJson,
    InvalidDescriptor,
    InvalidPolicy,
    InsufficientSignatures,
    SignatureInvalid,
    Expired,
    NotYetValid
}

public sealed record UpdateDescriptorVerificationResult(
    UpdateDescriptorStatus Status,
    VerifiedUpdateDescriptor? Verified,
    string Detail)
{
    public bool Succeeded => Status == UpdateDescriptorStatus.Verified && Verified is not null;
}

public sealed class VerifiedUpdateDescriptor
{
    internal VerifiedUpdateDescriptor(
        UpdateAcquisitionDescriptor descriptor,
        string descriptorSha256,
        IReadOnlyList<string> signerKeyIds,
        IReadOnlyDictionary<string, Uri> artifactUris,
        IReadOnlySet<string> allowedOrigins)
    {
        Descriptor = descriptor;
        DescriptorSha256 = descriptorSha256;
        SignerKeyIds = signerKeyIds;
        ArtifactUris = artifactUris;
        AllowedOrigins = allowedOrigins;
    }

    public UpdateAcquisitionDescriptor Descriptor { get; }
    public string DescriptorSha256 { get; }
    public IReadOnlyList<string> SignerKeyIds { get; }
    public IReadOnlyDictionary<string, Uri> ArtifactUris { get; }
    public IReadOnlySet<string> AllowedOrigins { get; }
}

public enum UpdateTrustTransitionStatus
{
    AcceptedUpgrade,
    AcceptedIdempotent,
    RejectedInvalid,
    RejectedRollback,
    RejectedEquivocation,
    RejectedInsufficientQuorum,
    RejectedKeyReplacement,
    RejectedKeyRevival,
    RejectedMetadataOverlap,
    RejectedReleaseKeyOverlap,
    RejectedTlsPinOverlap,
    RejectedPublisherOverlap,
    RejectedPrematureRetirement
}

public sealed record UpdateTrustTransitionDecision(
    UpdateTrustTransitionStatus Status,
    long CurrentSequence,
    long ProposedSequence,
    string? ProposedPolicySha256,
    IReadOnlyList<string> AuthorizingKeyIds,
    string Detail)
{
    public bool Accepted => Status is
        UpdateTrustTransitionStatus.AcceptedUpgrade or
        UpdateTrustTransitionStatus.AcceptedIdempotent;
}

public sealed record AcquiredUpdateArtifact(
    string Name,
    string FullPath,
    long Length,
    string Sha256,
    Uri InitialUri,
    Uri EffectiveUri,
    IReadOnlyList<Uri> Redirects,
    string TlsSpkiSha256,
    TimeSpan Duration);

public sealed class AcquiredUpdateBundle : IAsyncDisposable, IDisposable
{
    private readonly IReadOnlyDictionary<string, FileStream> _locks;
    private bool _disposed;

    internal AcquiredUpdateBundle(
        string rootPath,
        IReadOnlyDictionary<string, AcquiredUpdateArtifact> artifacts,
        IReadOnlyDictionary<string, FileStream> locks)
    {
        RootPath = rootPath;
        Artifacts = artifacts;
        _locks = locks;
    }

    public string RootPath { get; }
    public IReadOnlyDictionary<string, AcquiredUpdateArtifact> Artifacts { get; }

    internal async Task VerifyArtifactUnchangedAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Artifacts.TryGetValue(name, out AcquiredUpdateArtifact artifact) ||
            !_locks.TryGetValue(name, out FileStream lockedStream))
        {
            throw new InvalidDataException(
                "The requested acquired update artifact is unavailable.");
        }

        lockedStream.Position = 0;
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(
            lockedStream,
            cancellationToken).ConfigureAwait(false);
        if (lockedStream.Position != artifact.Length ||
            lockedStream.Length != artifact.Length ||
            !string.Equals(
                Convert.ToHexString(hash),
                artifact.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "An acquired update artifact changed after bounded download verification.");
        }

        lockedStream.Position = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (FileStream stream in _locks.Values)
        {
            stream.Dispose();
        }

        UpdatePrivateStaging.DeleteTree(RootPath);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (FileStream stream in _locks.Values)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        UpdatePrivateStaging.DeleteTree(RootPath);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public enum UpdateFileChangeKind
{
    Add,
    Replace,
    Remove,
    Unchanged
}

public sealed record UpdateFileChange(
    string Path,
    UpdateFileChangeKind Kind,
    long CurrentLength,
    long TargetLength,
    string? CurrentSha256,
    string? TargetSha256);

public sealed record UpdatePublisherEvidence(
    string Path,
    string ApprovedPublisher,
    string SubjectNameSha256,
    string SubjectPublicKeyInfoSha256,
    string FileSha256);

public sealed record UpdateDiskImpact(
    long DownloadBytes,
    long CurrentInstallBytes,
    long TargetInstallBytes,
    long AddedOrReplacedBytes,
    long RemovedBytes,
    long RecoveryReserveBytes,
    long EstimatedPeakAdditionalBytes);

public sealed record UpdateConfirmationChallenge(
    string PlanSha256,
    string RequiredPhrase,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsSatisfiedBy(
        string planSha256,
        string phrase,
        DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero &&
        nowUtc <= ExpiresAtUtc &&
        string.Equals(PlanSha256, planSha256, StringComparison.Ordinal) &&
        string.Equals(RequiredPhrase, phrase, StringComparison.Ordinal);
}

public sealed record SoltexUpdatePreview(
    string Product,
    string Channel,
    string CurrentVersion,
    string TargetVersion,
    long TargetSequence,
    string DescriptorSha256,
    string ManifestSha256,
    string PackageSha256,
    string ReleaseKeyId,
    IReadOnlyList<string> DescriptorSignerKeyIds,
    IReadOnlyList<UpdatePublisherEvidence> Publishers,
    IReadOnlyList<UpdateFileChange> FileChanges,
    UpdateDiskImpact DiskImpact,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RecoveryPrerequisites,
    string PlanSha256,
    UpdateConfirmationChallenge Confirmation);

public sealed record UpdatePlannerRequest(
    byte[] DescriptorBytes,
    IReadOnlyList<UpdateDetachedSignature> DescriptorSignatures,
    UpdateTrustPolicy TrustPolicy,
    string CurrentInstallRoot,
    string CurrentVersion,
    string SequenceStateRoot,
    string AcquisitionStagingParent,
    string JournalStateRoot,
    DateTimeOffset NowUtc);

public enum UpdatePlanningPhase
{
    Started,
    DescriptorVerified,
    ArtifactsAcquired,
    PackageStaged,
    ManifestVerified,
    PublishersAuthorized,
    PreviewReady,
    Failed,
    Cleaned
}

public sealed record UpdatePlanningJournalEntry(
    Guid AttemptId,
    UpdatePlanningPhase Phase,
    DateTimeOffset TimestampUtc,
    string? DescriptorSha256,
    string? ManifestSha256,
    string? PackageSha256,
    string? PrivateStagingToken,
    string Detail);

public sealed record UpdatePlanningRecoveryReport(
    IReadOnlyList<UpdatePlanningJournalEntry> Entries,
    IReadOnlyList<string> ExistingPrivateStagingTokens,
    bool HasIncompletePlanningAttempt,
    string Detail);
