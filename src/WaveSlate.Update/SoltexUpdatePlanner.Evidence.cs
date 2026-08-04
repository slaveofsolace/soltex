using System.Security.Cryptography;
using System.Text.Json;
using WaveSlate.Security;

namespace WaveSlate.Update;

public sealed partial class SoltexUpdatePlanner
{
    private static void ValidateExactStagedSet(
        StagedArchive staged,
        SignedReleaseManifest manifest)
    {
        Dictionary<string, StagedArchiveEntry> stagedByPath = staged.Entries.ToDictionary(
            item => item.RelativePath,
            StringComparer.Ordinal);
        Dictionary<string, IntegrityManifestFile> manifestByPath = manifest.Files.ToDictionary(
            item => item.Path,
            StringComparer.Ordinal);
        if (stagedByPath.Count != manifestByPath.Count ||
            !stagedByPath.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(manifestByPath.Keys))
        {
            throw new InvalidDataException(
                "The staged package contains an extra, missing, or non-exact manifest path.");
        }

        foreach ((string path, StagedArchiveEntry stagedEntry) in stagedByPath)
        {
            IntegrityManifestFile signed = manifestByPath[path];
            if (stagedEntry.Length != signed.Length ||
                !string.Equals(stagedEntry.Sha256, signed.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The staged package evidence differs from the signed manifest.");
            }
        }
    }

    private static async Task<List<UpdatePublisherEvidence>> AuthorizePublishersAsync(
        StagedArchive staged,
        PublisherPolicy policy,
        CancellationToken cancellationToken)
    {
        List<UpdatePublisherEvidence> publishers = [];
        foreach (StagedArchiveEntry entry in staged.Entries.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(entry.RelativePath);
            if (RejectedActiveExtensions.Contains(extension))
            {
                throw new InvalidDataException(
                    $"The release contains the unsupported active-content type '{extension}'.");
            }

            string fullPath = PathSafety.CombineUnderRoot(
                staged.RootPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            bool portableExecutable = await HasPortableExecutableHeaderAsync(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            if (!PublisherExtensions.Contains(extension))
            {
                if (portableExecutable)
                {
                    throw new InvalidDataException(
                        $"The release contains PE content under the unsupported extension '{extension}'.");
                }

                continue;
            }

            if (!portableExecutable)
            {
                throw new InvalidDataException(
                    $"The release labels non-PE content as '{extension}'.");
            }

            AuthenticodePublisherVerificationResult result =
                await AuthenticodePublisherVerifier.VerifyAsync(
                    fullPath,
                    policy,
                    allowNetworkRevocationRetrieval: false,
                    cancellationToken).ConfigureAwait(false);
            if (!result.IsApproved ||
                result.Policy?.ApprovedPublisher is null ||
                result.Signer is null ||
                !string.Equals(result.FileSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Publisher authorization failed for '{entry.RelativePath}': {UpdateText.Sanitize(result.Detail)}");
            }

            publishers.Add(new UpdatePublisherEvidence(
                entry.RelativePath,
                result.Policy.ApprovedPublisher,
                result.Signer.SubjectNameSha256,
                result.Signer.SubjectPublicKeyInfoSha256,
                result.FileSha256!));
        }

        return publishers;
    }

    private static async Task<bool> HasPortableExecutableHeaderAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            PathSafety.NormalizeExistingFile(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 2,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] marker = new byte[2];
        int offset = 0;
        while (offset < marker.Length)
        {
            int read = await stream.ReadAsync(
                marker.AsMemory(offset, marker.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return marker[0] == (byte)'M' && marker[1] == (byte)'Z';
    }

    private static async Task<IReadOnlyDictionary<string, InstallFileEvidence>> ScanCurrentInstallAsync(
        string root,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            return new Dictionary<string, InstallFileEvidence>(StringComparer.OrdinalIgnoreCase);
        }

        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The current installation root is a reparse point.");
        }

        Dictionary<string, InstallFileEvidence> files = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> directories = new();
        directories.Push(fullRoot);
        long total = 0;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = directories.Pop();
            foreach (string childDirectory in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("A reparse point exists inside the current installation.");
                }

                directories.Push(childDirectory);
            }

            foreach (string filePath in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo file = new(filePath);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("A reparse-point file exists inside the current installation.");
                }

                if (files.Count >= MaximumInstallFiles)
                {
                    throw new InvalidDataException("The current installation contains too many files.");
                }

                total = checked(total + file.Length);
                if (total > MaximumCurrentInstallBytes)
                {
                    throw new InvalidDataException("The current installation exceeds the planner size bound.");
                }

                string relative = Path.GetRelativePath(fullRoot, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!files.TryAdd(
                        relative,
                        new InstallFileEvidence(
                            relative,
                            file.Length,
                            await FileHashing.Sha256Async(filePath, cancellationToken)
                                .ConfigureAwait(false))))
                {
                    throw new InvalidDataException(
                        "The current installation contains case-colliding paths.");
                }
            }
        }

        return files;
    }

    private static List<UpdateFileChange> BuildChanges(
        IReadOnlyDictionary<string, InstallFileEvidence> current,
        SignedReleaseManifest manifest)
    {
        Dictionary<string, IntegrityManifestFile> target = manifest.Files.ToDictionary(
            item => item.Path,
            StringComparer.OrdinalIgnoreCase);
        List<UpdateFileChange> changes = [];
        foreach (IntegrityManifestFile targetFile in manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            if (!current.TryGetValue(targetFile.Path, out InstallFileEvidence? existing))
            {
                changes.Add(new UpdateFileChange(
                    targetFile.Path,
                    UpdateFileChangeKind.Add,
                    0,
                    targetFile.Length,
                    null,
                    targetFile.Sha256));
                continue;
            }

            bool unchanged = existing.Length == targetFile.Length &&
                string.Equals(existing.Sha256, targetFile.Sha256, StringComparison.OrdinalIgnoreCase);
            changes.Add(new UpdateFileChange(
                targetFile.Path,
                unchanged ? UpdateFileChangeKind.Unchanged : UpdateFileChangeKind.Replace,
                existing.Length,
                targetFile.Length,
                existing.Sha256,
                targetFile.Sha256));
        }

        foreach (InstallFileEvidence existing in current.Values
                     .Where(item => !target.ContainsKey(item.Path))
                     .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            changes.Add(new UpdateFileChange(
                existing.Path,
                UpdateFileChangeKind.Remove,
                existing.Length,
                0,
                existing.Sha256,
                null));
        }

        return changes
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Kind)
            .ToList();
    }

    private static UpdateDiskImpact BuildDiskImpact(
        AcquiredUpdateBundle acquired,
        IReadOnlyDictionary<string, InstallFileEvidence> current,
        SignedReleaseManifest manifest,
        IReadOnlyList<UpdateFileChange> changes)
    {
        long download = acquired.Artifacts.Values.Sum(item => item.Length);
        long currentBytes = current.Values.Sum(item => item.Length);
        long targetBytes = manifest.Files.Sum(item => item.Length);
        long addedOrReplaced = changes
            .Where(item => item.Kind is UpdateFileChangeKind.Add or UpdateFileChangeKind.Replace)
            .Sum(item => item.TargetLength);
        long removed = changes
            .Where(item => item.Kind == UpdateFileChangeKind.Remove)
            .Sum(item => item.CurrentLength);
        long recoveryReserve = checked(currentBytes + Math.Max(16L * 1024 * 1024, currentBytes / 10));
        long peak = checked(download + targetBytes + recoveryReserve);
        return new UpdateDiskImpact(
            download,
            currentBytes,
            targetBytes,
            addedOrReplaced,
            removed,
            recoveryReserve,
            peak);
    }

    private static List<string> BuildWarnings(
        ReleaseSequenceDecision sequence,
        IReadOnlyList<UpdateFileChange> changes)
    {
        List<string> warnings =
        [
            "This is a verified preview only. Soltex has no activation or installer path in this build.",
            "TLS, metadata, manifest, and publisher verification do not establish that the release is defect-free or suitable for deployment."
        ];
        if (sequence.Disposition == ReleaseSequenceDisposition.AcceptedFirstRelease)
        {
            warnings.Add("No prior accepted sequence exists for this channel; owner review is required before any future activation design.");
        }

        int removals = changes.Count(item => item.Kind == UpdateFileChangeKind.Remove);
        if (removals > 0)
        {
            warnings.Add($"The target file set would remove {removals} current installation file(s).");
        }

        return warnings;
    }

    private static List<string> BuildRecoveryPrerequisites(UpdateDiskImpact diskImpact) =>
    [
        $"Retain at least {diskImpact.RecoveryReserveBytes} bytes for a future last-known-good installation snapshot.",
        "A future activator must use a separate signed elevation boundary and atomic version switch.",
        "A future activator must prove rollback after interruption, locked files, disk-full conditions, reboot, and cancellation.",
        "Do not execute files from acquisition or archive-staging directories."
    ];

    private static SoltexUpdatePreview BuildPreview(
        string currentVersion,
        VerifiedUpdateDescriptor verified,
        SignedReleaseVerificationResult manifestResult,
        AcquiredUpdateBundle acquired,
        IReadOnlyList<UpdatePublisherEvidence> publishers,
        IReadOnlyList<UpdateFileChange> changes,
        UpdateDiskImpact diskImpact,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> recovery,
        DateTimeOffset nowUtc)
    {
        DeterministicPlanMaterial material = new(
            "Soltex",
            verified.Descriptor.Channel,
            currentVersion,
            verified.Descriptor.Version,
            verified.Descriptor.Sequence,
            verified.DescriptorSha256,
            manifestResult.ManifestSha256!,
            acquired.Artifacts["package"].Sha256,
            verified.Descriptor.ReleaseKeyId,
            verified.SignerKeyIds.Order(StringComparer.Ordinal).ToArray(),
            publishers.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            changes.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            diskImpact,
            warnings.ToArray(),
            recovery.ToArray());
        byte[] materialBytes = JsonSerializer.SerializeToUtf8Bytes(
            material,
            StrictJson.CreateSerializerOptions());
        string planHash = Convert.ToHexString(SHA256.HashData(materialBytes));
        DateTimeOffset challengeExpiry = verified.Descriptor.ExpiresAtUtc < nowUtc.AddMinutes(30)
            ? verified.Descriptor.ExpiresAtUtc
            : nowUtc.AddMinutes(30);
        UpdateConfirmationChallenge confirmation = new(
            planHash,
            $"CONFIRM SOLTEX UPDATE {verified.Descriptor.Version}",
            challengeExpiry);
        return new SoltexUpdatePreview(
            material.Product,
            material.Channel,
            material.CurrentVersion,
            material.TargetVersion,
            material.TargetSequence,
            material.DescriptorSha256,
            material.ManifestSha256,
            material.PackageSha256,
            material.ReleaseKeyId,
            material.DescriptorSignerKeyIds,
            material.Publishers,
            material.FileChanges,
            material.DiskImpact,
            material.Warnings,
            material.RecoveryPrerequisites,
            planHash,
            confirmation);
    }

    private static string ExtractPrivateToken(string rootPath)
    {
        string name = Path.GetFileName(rootPath);
        const string prefix = "soltex-update-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !UpdatePrivateStaging.IsToken(name[prefix.Length..]))
        {
            throw new InvalidDataException("The update acquisition root has an invalid private token.");
        }

        return name[prefix.Length..];
    }

    private static UpdatePlanningJournalEntry Entry(
        Guid attemptId,
        UpdatePlanningPhase phase,
        DateTimeOffset timestampUtc,
        string? descriptorSha256,
        string? manifestSha256,
        string? packageSha256,
        string? token,
        string detail) =>
        new(
            attemptId,
            phase,
            timestampUtc,
            descriptorSha256,
            manifestSha256,
            packageSha256,
            token,
            UpdateText.Sanitize(detail));

    private static async Task TryRecordFailureAsync(
        UpdatePlanningJournal journal,
        Guid attemptId,
        DateTimeOffset timestampUtc,
        string? descriptorSha256,
        string? manifestSha256,
        string? packageSha256,
        string? token,
        string detail)
    {
        try
        {
            await journal.AppendAsync(
                Entry(
                    attemptId,
                    UpdatePlanningPhase.Failed,
                    timestampUtc,
                    descriptorSha256,
                    manifestSha256,
                    packageSha256,
                    token,
                    detail)).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            CryptographicException or
            ArgumentException)
        {
        }
    }

    private sealed record InstallFileEvidence(
        string Path,
        long Length,
        string Sha256);

    private sealed record DeterministicPlanMaterial(
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
        IReadOnlyList<string> RecoveryPrerequisites);
}
