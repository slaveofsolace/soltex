using System.Security.Cryptography;
using WaveSlate.Security;

namespace WaveSlate.Update;

public sealed partial class SoltexUpdatePlanner
{
    private const int MaximumInstallFiles = 10_000;
    private const long MaximumCurrentInstallBytes = 8L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> PublisherExtensions = new(
        [".exe", ".dll"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RejectedActiveExtensions = new(
        [
            ".bat", ".cmd", ".com", ".cpl", ".hta", ".inf", ".js", ".jse",
            ".lnk", ".msi", ".msp", ".ps1", ".psd1", ".psm1", ".reg",
            ".scr", ".sys", ".url", ".vbe", ".vbs", ".wsf", ".wsh"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly BoundedHttpsAcquirer _acquirer;
    private readonly BoundedArchiveStager _archiveStager;

    public SoltexUpdatePlanner(
        IAuthenticatedHttpsTransport transport,
        ArchiveStagingLimits? archiveLimits = null)
    {
        _acquirer = new BoundedHttpsAcquirer(transport);
        _archiveStager = new BoundedArchiveStager(archiveLimits);
    }

    public async Task<PreparedSoltexUpdate> PlanAsync(
        UpdatePlannerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        UpdateTrustPolicyValidator.RequireUtc(request.NowUtc, nameof(request.NowUtc));
        string currentVersion = UpdateText.BoundIdentifier(
            request.CurrentVersion,
            nameof(request.CurrentVersion),
            maximum: 64);
        Guid attemptId = Guid.NewGuid();
        using UpdatePlanningJournal journal = new(request.JournalStateRoot);
        AcquiredUpdateBundle? acquired = null;
        StagedArchive? staged = null;
        string? token = null;
        string? descriptorHash = null;
        string? manifestHash = null;
        string? packageHash = null;
        try
        {
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.Started, request.NowUtc, null, null, null, null,
                    "A non-installing update planning attempt started."),
                cancellationToken).ConfigureAwait(false);

            UpdateDescriptorVerificationResult descriptorResult = UpdateDescriptorVerifier.Verify(
                request.DescriptorBytes,
                request.DescriptorSignatures,
                request.TrustPolicy,
                request.NowUtc);
            if (!descriptorResult.Succeeded)
            {
                throw new InvalidDataException(descriptorResult.Detail);
            }

            VerifiedUpdateDescriptor verified = descriptorResult.Verified!;
            descriptorHash = verified.DescriptorSha256;
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.DescriptorVerified, request.NowUtc,
                    descriptorHash, null, null, null,
                    "The acquisition descriptor and metadata-signature quorum were verified."),
                cancellationToken).ConfigureAwait(false);

            acquired = await _acquirer.AcquireAsync(
                verified,
                request.TrustPolicy,
                request.AcquisitionStagingParent,
                request.NowUtc,
                cancellationToken).ConfigureAwait(false);
            token = ExtractPrivateToken(acquired.RootPath);
            packageHash = acquired.Artifacts["package"].Sha256;
            await acquired.VerifyArtifactUnchangedAsync("manifest", cancellationToken)
                .ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("signature", cancellationToken)
                .ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("package", cancellationToken)
                .ConfigureAwait(false);
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.ArtifactsAcquired, request.NowUtc,
                    descriptorHash, null, packageHash, token,
                    "All signed descriptor artifacts were acquired as inert bounded bytes."),
                cancellationToken).ConfigureAwait(false);

            await acquired.VerifyArtifactUnchangedAsync("package", cancellationToken)
                .ConfigureAwait(false);
            staged = await _archiveStager.StageZipAsync(
                acquired.Artifacts["package"].FullPath,
                Path.Combine(acquired.RootPath, "expanded"),
                cancellationToken).ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("package", cancellationToken)
                .ConfigureAwait(false);
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.PackageStaged, request.NowUtc,
                    descriptorHash, null, packageHash, token,
                    "The inert package passed bounded archive staging."),
                cancellationToken).ConfigureAwait(false);

            await acquired.VerifyArtifactUnchangedAsync("manifest", cancellationToken)
                .ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("signature", cancellationToken)
                .ConfigureAwait(false);
            byte[] manifestBytes = await File.ReadAllBytesAsync(
                acquired.Artifacts["manifest"].FullPath,
                cancellationToken).ConfigureAwait(false);
            StrictJson.ValidateNoDuplicateProperties(manifestBytes);
            ValidatedUpdateTrustPolicy trust = UpdateTrustPolicyValidator.Validate(
                request.TrustPolicy,
                request.NowUtc);
            UpdateRsaTrustKey releaseKey = trust.ReleaseKeys[verified.Descriptor.ReleaseKeyId];
            SignedReleaseVerificationResult manifestResult =
                await SignedReleaseManifestVerifier.VerifyAsync(
                    staged.RootPath,
                    acquired.Artifacts["manifest"].FullPath,
                    acquired.Artifacts["signature"].FullPath,
                    releaseKey.PublicKeyPem,
                    cancellationToken).ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("manifest", cancellationToken)
                .ConfigureAwait(false);
            await acquired.VerifyArtifactUnchangedAsync("signature", cancellationToken)
                .ConfigureAwait(false);
            if (!manifestResult.Succeeded || manifestResult.Manifest is null)
            {
                throw new InvalidDataException(
                    "The signed release manifest did not verify: " +
                    UpdateText.Sanitize(string.Join("; ", manifestResult.Errors)));
            }

            SignedReleaseManifest manifest = manifestResult.Manifest;
            manifestHash = manifestResult.ManifestSha256;
            if (!string.Equals(manifest.Channel, verified.Descriptor.Channel, StringComparison.Ordinal) ||
                manifest.Sequence != verified.Descriptor.Sequence ||
                !string.Equals(manifest.Version, verified.Descriptor.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The signed manifest identity does not match the signed acquisition descriptor.");
            }

            ValidateExactStagedSet(staged, manifest);
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.ManifestVerified, request.NowUtc,
                    descriptorHash, manifestHash, packageHash, token,
                    "The exact staged file set matches the signed release manifest."),
                cancellationToken).ConfigureAwait(false);

            List<UpdatePublisherEvidence> publishers = await AuthorizePublishersAsync(
                staged,
                trust.PublisherPolicy,
                cancellationToken).ConfigureAwait(false);
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.PublishersAuthorized, request.NowUtc,
                    descriptorHash, manifestHash, packageHash, token,
                    "All executable release files matched approved Windows publisher identities."),
                cancellationToken).ConfigureAwait(false);

            using ReleaseSequenceStore sequenceStore = new(request.SequenceStateRoot);
            ReleaseSequenceDecision sequenceDecision = await sequenceStore.EvaluateVerifiedAsync(
                manifestResult,
                cancellationToken).ConfigureAwait(false);
            if (!sequenceDecision.Accepted)
            {
                throw new InvalidDataException(sequenceDecision.Detail);
            }

            IReadOnlyDictionary<string, InstallFileEvidence> currentFiles =
                await ScanCurrentInstallAsync(
                    request.CurrentInstallRoot,
                    cancellationToken).ConfigureAwait(false);
            List<UpdateFileChange> changes = BuildChanges(currentFiles, manifest);
            UpdateDiskImpact diskImpact = BuildDiskImpact(
                acquired,
                currentFiles,
                manifest,
                changes);
            List<string> warnings = BuildWarnings(sequenceDecision, changes);
            List<string> recovery = BuildRecoveryPrerequisites(diskImpact);
            SoltexUpdatePreview preview = BuildPreview(
                currentVersion,
                verified,
                manifestResult,
                acquired,
                publishers,
                changes,
                diskImpact,
                warnings,
                recovery,
                request.NowUtc);
            await journal.AppendAsync(
                Entry(attemptId, UpdatePlanningPhase.PreviewReady, request.NowUtc,
                    descriptorHash, manifestHash, packageHash, token,
                    "A deterministic update preview is ready; no activation or execution occurred."),
                cancellationToken).ConfigureAwait(false);

            PreparedSoltexUpdate prepared = new(preview, staged, acquired);
            staged = null;
            acquired = null;
            return prepared;
        }
        catch (OperationCanceledException)
        {
            await TryRecordFailureAsync(
                journal,
                attemptId,
                request.NowUtc,
                descriptorHash,
                manifestHash,
                packageHash,
                token,
                "Update planning was cancelled before activation; no activation capability exists.")
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            CryptographicException or
            ArgumentException or
            TimeoutException or
            HttpRequestException or
            OverflowException)
        {
            await TryRecordFailureAsync(
                journal,
                attemptId,
                request.NowUtc,
                descriptorHash,
                manifestHash,
                packageHash,
                token,
                UpdateText.Sanitize(exception.Message))
                .ConfigureAwait(false);
            throw new InvalidDataException(
                "The Soltex update plan could not be produced: " +
                UpdateText.Sanitize(exception.Message),
                exception);
        }
        finally
        {
            if (staged is not null)
            {
                await staged.DisposeAsync().ConfigureAwait(false);
            }

            if (acquired is not null)
            {
                await acquired.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
