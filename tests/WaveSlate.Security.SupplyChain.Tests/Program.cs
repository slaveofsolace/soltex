using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using WaveSlate.Security;

List<(string Name, Func<Task> Test)> tests =
[
    ("Publisher policy accepts an exact code-signing identity", PublisherPolicyAcceptsExactIdentityAsync),
    ("Publisher policy rejects the same subject with a different key", PublisherPolicyRejectsKeyMismatchAsync),
    ("Publisher policy requires the code-signing EKU", PublisherPolicyRequiresCodeSigningUsageAsync),
    ("Authenticode publisher verification pins one trusted .NET host signature", AuthenticodePublisherPinsDotNetHostAsync),
    ("Signed release state accepts a monotonic upgrade", SignedReleaseAcceptsUpgradeAsync),
    ("Signed release state rejects rollback", SignedReleaseRejectsRollbackAsync),
    ("Signed release state is idempotent for identical bytes", SignedReleaseIsIdempotentAsync),
    ("Signed release state rejects sequence equivocation", SignedReleaseRejectsEquivocationAsync),
    ("Signed release state detects local tampering", SignedReleaseStateDetectsTamperingAsync),
    ("Signed release rejects noncanonical file paths", SignedReleaseRejectsNonCanonicalPathsAsync),
    ("Signed release requires an explicit UTC publication time", SignedReleaseRequiresUtcPublicationTimeAsync),
    ("Bounded ZIP staging preserves benign bytes", ArchiveStagingPreservesBenignBytesAsync),
    ("Bounded ZIP staging rejects traversal and cleans up", ArchiveStagingRejectsTraversalAsync),
    ("Bounded ZIP staging rejects case-colliding paths", ArchiveStagingRejectsCaseCollisionAsync),
    ("Bounded ZIP staging rejects symbolic-link entries", ArchiveStagingRejectsSymbolicLinksAsync),
    ("Bounded ZIP staging enforces expanded-size limits", ArchiveStagingEnforcesSizeLimitsAsync),
    ("Bounded ZIP staging enforces compression-ratio limits", ArchiveStagingEnforcesCompressionRatioAsync)
];

int failed = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine("      " + exception.Message);
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;

static Task PublisherPolicyAcceptsExactIdentityAsync()
{
    using X509Certificate2 certificate = CreateTestCertificate(
        "CN=Soltex Publisher Unit Test",
        includeCodeSigningUsage: true);
    PublisherIdentityEvidence evidence = PublisherIdentityEvidence.FromCertificate(certificate);
    PublisherPolicy policy = new(
    [
        new ApprovedPublisherIdentity(
            "Soltex test publisher",
            evidence.SubjectNameSha256,
            evidence.SubjectPublicKeyInfoSha256)
    ]);

    PublisherPolicyResult result = policy.Evaluate(evidence);
    True(result.IsApproved, result.Detail);
    Equal("Soltex test publisher", result.ApprovedPublisher);
    return Task.CompletedTask;
}

static Task PublisherPolicyRejectsKeyMismatchAsync()
{
    using X509Certificate2 approvedCertificate = CreateTestCertificate(
        "CN=Soltex Publisher Unit Test",
        includeCodeSigningUsage: true);
    using X509Certificate2 otherCertificate = CreateTestCertificate(
        "CN=Soltex Publisher Unit Test",
        includeCodeSigningUsage: true);
    PublisherIdentityEvidence approvedEvidence =
        PublisherIdentityEvidence.FromCertificate(approvedCertificate);
    PublisherIdentityEvidence otherEvidence =
        PublisherIdentityEvidence.FromCertificate(otherCertificate);
    Equal(approvedEvidence.SubjectNameSha256, otherEvidence.SubjectNameSha256);
    True(
        !string.Equals(
            approvedEvidence.SubjectPublicKeyInfoSha256,
            otherEvidence.SubjectPublicKeyInfoSha256,
            StringComparison.Ordinal),
        "The independently generated certificate keys unexpectedly matched.");

    PublisherPolicy policy = new(
    [
        new ApprovedPublisherIdentity(
            "Soltex test publisher",
            approvedEvidence.SubjectNameSha256,
            approvedEvidence.SubjectPublicKeyInfoSha256)
    ]);
    PublisherPolicyResult result = policy.Evaluate(otherEvidence);
    Equal(PublisherPolicyStatus.PublisherNotApproved, result.Status);
    True(!result.IsApproved, "A different signing key must not satisfy the publisher pin.");
    return Task.CompletedTask;
}

static Task PublisherPolicyRequiresCodeSigningUsageAsync()
{
    using X509Certificate2 certificate = CreateTestCertificate(
        "CN=Soltex Publisher Unit Test",
        includeCodeSigningUsage: false);
    PublisherIdentityEvidence evidence = PublisherIdentityEvidence.FromCertificate(certificate);
    PublisherPolicy policy = new(
    [
        new ApprovedPublisherIdentity(
            "Soltex test publisher",
            evidence.SubjectNameSha256,
            evidence.SubjectPublicKeyInfoSha256)
    ]);

    PublisherPolicyResult result = policy.Evaluate(evidence);
    Equal(PublisherPolicyStatus.MissingCodeSigningUsage, result.Status);
    True(!result.IsApproved, "A signer without an explicit code-signing EKU must be rejected.");
    return Task.CompletedTask;
}

static async Task AuthenticodePublisherPinsDotNetHostAsync()
{
    string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
    string dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
    string signedHostPath = Path.Combine(dotnetRoot, "dotnet.exe");
    AuthenticodeSignerIdentityResult identity =
        AuthenticodeSignerIdentityReader.Read(signedHostPath);
    True(identity.Succeeded && identity.Evidence is not null, identity.Detail);
    True(
        identity.Evidence!.HasCodeSigningEnhancedKeyUsage,
        "The signed .NET host signer must expose the code-signing EKU for this test.");

    PublisherPolicy policy = new(
    [
        new ApprovedPublisherIdentity(
            "Observed Microsoft .NET host signer",
            identity.Evidence.SubjectNameSha256,
            identity.Evidence.SubjectPublicKeyInfoSha256)
    ]);
    AuthenticodePublisherVerificationResult result =
        await AuthenticodePublisherVerifier.VerifyAsync(signedHostPath, policy);
    True(result.IsApproved, result.Detail);
    Equal(AuthenticodePublisherStatus.Approved, result.Status);
    True(result.Authenticode is not null, "Publisher verification did not retain WinTrust evidence.");
    Equal((uint?)0, result.Authenticode!.VerifiedSignatureIndex);
    Equal((uint?)0, result.Authenticode.SecondarySignatureCount);
    True(
        !string.IsNullOrWhiteSpace(result.FileSha256) && result.FileSha256.Length == 64,
        "Publisher verification should retain the verified file SHA-256.");
}

static async Task SignedReleaseAcceptsUpgradeAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult first = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 1,
            version: "1.0.0",
            payload: "first release");
        SignedReleaseVerificationResult second = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 2,
            version: "1.1.0",
            payload: "second release");

        using ReleaseSequenceStore store = new(Path.Combine(root, "state"));
        ReleaseSequenceDecision firstDecision = await store.AcceptVerifiedAsync(first);
        ReleaseSequenceDecision secondDecision = await store.AcceptVerifiedAsync(second);
        Equal(ReleaseSequenceDisposition.AcceptedFirstRelease, firstDecision.Disposition);
        Equal(ReleaseSequenceDisposition.AcceptedUpgrade, secondDecision.Disposition);
        Equal(1L, secondDecision.HighestAcceptedSequence);
        IReadOnlyList<AcceptedReleaseSequence> accepted = await store.ListAsync();
        Equal(1, accepted.Count);
        Equal(2L, accepted[0].Sequence);
    });
}

static async Task SignedReleaseRejectsRollbackAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult newer = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 7,
            version: "2.0.0",
            payload: "newer");
        SignedReleaseVerificationResult older = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 6,
            version: "1.9.0",
            payload: "older");

        using ReleaseSequenceStore store = new(Path.Combine(root, "state"));
        True((await store.AcceptVerifiedAsync(newer)).Accepted, "The initial verified release was rejected.");
        ReleaseSequenceDecision rollback = await store.AcceptVerifiedAsync(older);
        Equal(ReleaseSequenceDisposition.RejectedRollback, rollback.Disposition);
        True(!rollback.Accepted, "A lower release sequence must not be accepted.");
    });
}

static async Task SignedReleaseIsIdempotentAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult release = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 10,
            version: "3.0.0",
            payload: "same bytes");

        using ReleaseSequenceStore store = new(Path.Combine(root, "state"));
        True((await store.AcceptVerifiedAsync(release)).Accepted, "The initial release was rejected.");
        ReleaseSequenceDecision repeated = await store.AcceptVerifiedAsync(release);
        Equal(ReleaseSequenceDisposition.AcceptedIdempotent, repeated.Disposition);
        True(repeated.Accepted, "The exact same signed manifest should be idempotent.");
    });
}

static async Task SignedReleaseRejectsEquivocationAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult first = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 12,
            version: "4.0.0",
            payload: "payload A");
        SignedReleaseVerificationResult conflicting = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 12,
            version: "4.0.1",
            payload: "payload B");

        using ReleaseSequenceStore store = new(Path.Combine(root, "state"));
        True((await store.AcceptVerifiedAsync(first)).Accepted, "The initial release was rejected.");
        ReleaseSequenceDecision equivocation = await store.AcceptVerifiedAsync(conflicting);
        Equal(ReleaseSequenceDisposition.RejectedEquivocation, equivocation.Disposition);
        True(!equivocation.Accepted, "A different manifest must not reuse an accepted sequence.");
    });
}

static async Task SignedReleaseStateDetectsTamperingAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult release = await CreateVerifiedReleaseAsync(
            root,
            rsa,
            sequence: 1,
            version: "1.0.0",
            payload: "state fixture");
        string stateRoot = Path.Combine(root, "state");
        using (ReleaseSequenceStore store = new(stateRoot))
        {
            True((await store.AcceptVerifiedAsync(release)).Accepted, "The release was rejected.");
        }

        string statePath = Path.Combine(stateRoot, "release-sequences.json");
        await File.AppendAllTextAsync(statePath, " ");
        using ReleaseSequenceStore reopened = new(stateRoot);
        await ThrowsAsync<InvalidDataException>(() => reopened.ListAsync());
    });
}

static async Task SignedReleaseRejectsNonCanonicalPathsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        SignedReleaseVerificationResult verification = await CreateReleaseVerificationAsync(
            root,
            rsa,
            sequence: 20,
            version: "5.0.0",
            payload: "canonical path fixture",
            manifestFilePath: "./plugin.dll");
        True(!verification.Succeeded, "A signed alias path unexpectedly verified.");
        True(
            verification.Errors.Any(error =>
                error.Contains("not canonical", StringComparison.OrdinalIgnoreCase)),
            "The signed alias path did not produce a canonical-path error.");
    });
}

static async Task SignedReleaseRequiresUtcPublicationTimeAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using RSA rsa = RSA.Create(2_048);
        DateTimeOffset nonUtcTimestamp = new(
            2026,
            8,
            3,
            12,
            0,
            0,
            TimeSpan.FromHours(-5));
        SignedReleaseVerificationResult verification = await CreateReleaseVerificationAsync(
            root,
            rsa,
            sequence: 21,
            version: "5.0.1",
            payload: "timestamp fixture",
            publishedAtUtc: nonUtcTimestamp);
        True(!verification.Succeeded, "A non-UTC publication timestamp unexpectedly verified.");
        True(
            verification.Errors.Any(error =>
                error.Contains("UTC timestamp", StringComparison.OrdinalIgnoreCase)),
            "The non-UTC timestamp did not produce the expected verification error.");
    });
}

static async Task ArchiveStagingPreservesBenignBytesAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        byte[] expected = RandomNumberGenerator.GetBytes(8_192);
        string archivePath = Path.Combine(root, "fixture.zip");
        CreateZip(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "plugins/example/plugin.bin",
                CompressionLevel.Fastest);
            using Stream stream = entry.Open();
            stream.Write(expected);
        });

        string stagingParent = Path.Combine(root, "staging");
        BoundedArchiveStager stager = new();
        StagedArchive staged = await stager.StageZipAsync(archivePath, stagingParent);
        string stagedRoot = staged.RootPath;
        try
        {
            Equal(1, staged.Entries.Count);
            Equal("plugins/example/plugin.bin", staged.Entries[0].RelativePath);
            string outputPath = Path.Combine(
                staged.RootPath,
                "plugins",
                "example",
                "plugin.bin");
            SequenceEqual(expected, await File.ReadAllBytesAsync(outputPath));
            Equal(
                Convert.ToHexString(SHA256.HashData(expected)),
                staged.Entries[0].Sha256);
        }
        finally
        {
            await staged.DisposeAsync();
        }

        True(!Directory.Exists(stagedRoot), "Successful staging cleanup did not remove its private root.");
    });
}

static async Task ArchiveStagingRejectsTraversalAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "traversal.zip");
        CreateZip(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            using StreamWriter writer = new(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("blocked");
        });

        string stagingParent = Path.Combine(root, "staging");
        BoundedArchiveStager stager = new();
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, stagingParent));
        True(
            !Directory.EnumerateDirectories(stagingParent).Any(),
            "Failed traversal staging left a private extraction directory behind.");
        True(!File.Exists(Path.Combine(root, "outside.txt")), "Traversal wrote outside staging.");
    });
}

static async Task ArchiveStagingRejectsCaseCollisionAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "collision.zip");
        CreateZip(archivePath, archive =>
        {
            WriteTextEntry(archive, "Plugins/item.txt", "one");
            WriteTextEntry(archive, "plugins/item.txt", "two");
        });

        string stagingParent = Path.Combine(root, "staging");
        BoundedArchiveStager stager = new();
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, stagingParent));
        True(
            !Directory.EnumerateDirectories(stagingParent).Any(),
            "Case-collision rejection left a staging directory behind.");
    });
}

static async Task ArchiveStagingRejectsSymbolicLinksAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "symlink.zip");
        CreateZip(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("link");
            entry.ExternalAttributes = unchecked((0xA000 | 0x1FF) << 16);
            using StreamWriter writer = new(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("target");
        });

        string stagingParent = Path.Combine(root, "staging");
        BoundedArchiveStager stager = new();
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, stagingParent));
        True(
            !Directory.EnumerateDirectories(stagingParent).Any(),
            "Symbolic-link rejection left a staging directory behind.");
    });
}

static async Task ArchiveStagingEnforcesSizeLimitsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "oversized.zip");
        byte[] payload = RandomNumberGenerator.GetBytes(128);
        CreateZip(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("payload.bin", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            stream.Write(payload);
        });

        ArchiveStagingLimits limits = new(
            MaximumArchiveBytes: 1024 * 1024,
            MaximumEntries: 8,
            MaximumEntryBytes: 64,
            MaximumTotalExpandedBytes: 256,
            MaximumCompressionRatio: 200,
            MaximumRelativePathLength: 240,
            MaximumPathDepth: 16);
        BoundedArchiveStager stager = new(limits);
        string stagingParent = Path.Combine(root, "staging");
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, stagingParent));
        True(
            !Directory.EnumerateDirectories(stagingParent).Any(),
            "Expanded-size rejection left a staging directory behind.");
    });
}

static async Task ArchiveStagingEnforcesCompressionRatioAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "ratio.zip");
        byte[] payload = new byte[32 * 1024];
        CreateZip(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("payload.bin", CompressionLevel.SmallestSize);
            using Stream stream = entry.Open();
            stream.Write(payload);
        });

        ArchiveStagingLimits limits = new(
            MaximumArchiveBytes: 1024 * 1024,
            MaximumEntries: 8,
            MaximumEntryBytes: 64 * 1024,
            MaximumTotalExpandedBytes: 64 * 1024,
            MaximumCompressionRatio: 2,
            MaximumRelativePathLength: 240,
            MaximumPathDepth: 16);
        BoundedArchiveStager stager = new(limits);
        string stagingParent = Path.Combine(root, "staging");
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, stagingParent));
        True(
            !Directory.EnumerateDirectories(stagingParent).Any(),
            "Compression-ratio rejection left a staging directory behind.");
    });
}

static X509Certificate2 CreateTestCertificate(
    string subject,
    bool includeCodeSigningUsage)
{
    using RSA key = RSA.Create(2_048);
    CertificateRequest request = new(
        subject,
        key,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
        certificateAuthority: false,
        hasPathLengthConstraint: false,
        pathLengthConstraint: 0,
        critical: true));
    if (includeCodeSigningUsage)
    {
        OidCollection usages = new();
        usages.Add(new Oid("1.3.6.1.5.5.7.3.3", "Code Signing"));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            usages,
            critical: false));
    }

    return request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddDays(1));
}

static async Task<SignedReleaseVerificationResult> CreateVerifiedReleaseAsync(
    string root,
    RSA signingKey,
    long sequence,
    string version,
    string payload)
{
    SignedReleaseVerificationResult verification = await CreateReleaseVerificationAsync(
        root,
        signingKey,
        sequence,
        version,
        payload);
    True(verification.Succeeded, string.Join("; ", verification.Errors));
    Equal(1, verification.VerifiedFileCount);
    Equal("plugin.dll", verification.Manifest?.Files.Single().Path);
    return verification;
}

static async Task<SignedReleaseVerificationResult> CreateReleaseVerificationAsync(
    string root,
    RSA signingKey,
    long sequence,
    string version,
    string payload,
    string manifestFilePath = "plugin.dll",
    DateTimeOffset? publishedAtUtc = null)
{
    string fixtureRoot = Path.Combine(
        root,
        $"release-{sequence}-{Guid.NewGuid():N}");
    string contentRoot = Path.Combine(fixtureRoot, "content");
    Directory.CreateDirectory(contentRoot);
    string contentPath = Path.Combine(contentRoot, "plugin.dll");
    await File.WriteAllTextAsync(contentPath, payload, Encoding.UTF8);
    FileInfo contentInfo = new(contentPath);
    SignedReleaseManifest manifest = new(
        SchemaVersion: 2,
        Product: "Soltex",
        Channel: "stable",
        Sequence: sequence,
        Version: version,
        PublishedAtUtc: publishedAtUtc ?? DateTimeOffset.UtcNow,
        Files:
        [
            new IntegrityManifestFile(
                manifestFilePath,
                contentInfo.Length,
                await FileHashing.Sha256Async(contentPath))
        ]);

    JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
    byte[] signature = signingKey.SignData(
        manifestBytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pss);
    string releaseManifestPath = Path.Combine(fixtureRoot, "release.json");
    string signaturePath = Path.Combine(fixtureRoot, "release.sig");
    await File.WriteAllBytesAsync(releaseManifestPath, manifestBytes);
    await File.WriteAllBytesAsync(signaturePath, signature);

    return await SignedReleaseManifestVerifier.VerifyAsync(
        contentRoot,
        releaseManifestPath,
        signaturePath,
        signingKey.ExportSubjectPublicKeyInfoPem());
}

static void CreateZip(string path, Action<ZipArchive> populate)
{
    using FileStream stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false);
    populate(archive);
}

static void WriteTextEntry(ZipArchive archive, string path, string value)
{
    ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
    using StreamWriter writer = new(entry.Open(), Encoding.UTF8, leaveOpen: false);
    writer.Write(value);
}

static async Task WithTempDirectoryAsync(Func<string, Task> action)
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "WaveSlate.SupplyChain.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        await action(root);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void SequenceEqual(byte[] expected, byte[] actual)
{
    if (!expected.AsSpan().SequenceEqual(actual))
    {
        throw new InvalidOperationException("Byte sequences differ.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
