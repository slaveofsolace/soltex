using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaveSlate.Security;
using WaveSlate.Update;

List<(string Name, Func<Task> Test)> tests =
[
    ("A valid signed acquisition descriptor verifies", ValidDescriptorVerifiesAsync),
    ("Duplicate descriptor properties fail before authorization", DuplicateDescriptorPropertiesFailAsync),
    ("Expired descriptors retain an explicit classification", ExpiredDescriptorIsClassifiedAsync),
    ("Descriptor signature quorum is enforced", DescriptorQuorumIsEnforcedAsync),
    ("Unsigned redirect origins are rejected", UnsignedRedirectOriginIsRejectedAsync),
    ("Exact trust-policy replay is idempotent", ExactTrustReplayIsIdempotentAsync),
    ("Trust-policy rollback is rejected", TrustRollbackIsRejectedAsync),
    ("Trust-policy sequence equivocation is rejected", TrustEquivocationIsRejectedAsync),
    ("A signed overlap-preserving trust upgrade is accepted", TrustUpgradeIsAcceptedAsync),
    ("Trust key IDs cannot silently change key material", TrustKeyReplacementIsRejectedAsync),
    ("Bounded acquisition retains exact locked bytes and cleans up", ExactAcquisitionLocksAndCleansAsync),
    ("Acquisition rejects an unauthorized redirect and cleans up", UnauthorizedRedirectCleansAsync),
    ("Acquisition rejects a truncated body and cleans up", TruncatedBodyCleansAsync),
    ("Acquisition cancellation cleans private staging", AcquisitionCancellationCleansAsync),
    ("Planning journal sanitizes untrusted detail", PlanningJournalSanitizesDetailAsync),
    ("Planning recovery identifies and removes only private artifacts", PlanningRecoveryCleansPrivateArtifactsAsync),
    ("Update confirmation is exact and expires", UpdateConfirmationIsExactAsync)
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

static Task ValidDescriptorVerifiesAsync()
{
    using UpdateFixture fixture = new();
    Dictionary<string, byte[]> payloads = CreatePayloads();
    UpdateAcquisitionDescriptor descriptor = fixture.CreateDescriptor(payloads);
    byte[] bytes = fixture.Serialize(descriptor);
    UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
        bytes,
        [fixture.Sign("metadata-1", fixture.MetadataKey, bytes)],
        fixture.CreatePolicy(),
        fixture.NowUtc);

    True(result.Succeeded, result.Detail);
    Equal(UpdateDescriptorStatus.Verified, result.Status);
    Equal(3, result.Verified!.ArtifactUris.Count);
    True(result.Verified.AllowedOrigins.SetEquals([UpdateFixture.Origin]),
        "The verified descriptor origin set was not exact.");
    return Task.CompletedTask;
}

static Task DuplicateDescriptorPropertiesFailAsync()
{
    using UpdateFixture fixture = new();
    byte[] bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}");
    UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
        bytes,
        [],
        fixture.CreatePolicy(),
        fixture.NowUtc);
    Equal(UpdateDescriptorStatus.InvalidJson, result.Status);
    True(!result.Succeeded, "Duplicate descriptor properties were accepted.");
    return Task.CompletedTask;
}

static Task ExpiredDescriptorIsClassifiedAsync()
{
    using UpdateFixture fixture = new();
    UpdateAcquisitionDescriptor descriptor = fixture.CreateDescriptor(CreatePayloads()) with
    {
        IssuedAtUtc = fixture.NowUtc.AddHours(-2),
        ExpiresAtUtc = fixture.NowUtc.AddHours(-1)
    };
    byte[] bytes = fixture.Serialize(descriptor);
    UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
        bytes,
        [fixture.Sign("metadata-1", fixture.MetadataKey, bytes)],
        fixture.CreatePolicy(),
        fixture.NowUtc);
    Equal(UpdateDescriptorStatus.Expired, result.Status);
    return Task.CompletedTask;
}

static Task DescriptorQuorumIsEnforcedAsync()
{
    using UpdateFixture fixture = new();
    UpdateAcquisitionDescriptor descriptor = fixture.CreateDescriptor(CreatePayloads());
    byte[] bytes = fixture.Serialize(descriptor);
    UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
        bytes,
        [fixture.Sign("metadata-1", fixture.MetadataKey, bytes)],
        fixture.CreatePolicy(quorum: 2, includeSecondMetadataKey: true),
        fixture.NowUtc);
    Equal(UpdateDescriptorStatus.InsufficientSignatures, result.Status);
    return Task.CompletedTask;
}

static Task UnsignedRedirectOriginIsRejectedAsync()
{
    using UpdateFixture fixture = new();
    UpdateAcquisitionDescriptor descriptor = fixture.CreateDescriptor(CreatePayloads()) with
    {
        AllowedRedirectOrigins = ["https://redirect.soltex.invalid"]
    };
    byte[] bytes = fixture.Serialize(descriptor);
    UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
        bytes,
        [fixture.Sign("metadata-1", fixture.MetadataKey, bytes)],
        fixture.CreatePolicy(),
        fixture.NowUtc);
    Equal(UpdateDescriptorStatus.InvalidDescriptor, result.Status);
    return Task.CompletedTask;
}

static Task ExactTrustReplayIsIdempotentAsync()
{
    using UpdateFixture fixture = new();
    byte[] current = fixture.Serialize(fixture.CreatePolicy());
    UpdateTrustTransitionDecision decision = UpdateTrustTransitionEvaluator.Evaluate(
        current,
        current,
        [],
        fixture.NowUtc);
    Equal(UpdateTrustTransitionStatus.AcceptedIdempotent, decision.Status);
    return Task.CompletedTask;
}

static Task TrustRollbackIsRejectedAsync()
{
    using UpdateFixture fixture = new();
    byte[] current = fixture.Serialize(fixture.CreatePolicy(sequence: 2));
    byte[] proposed = fixture.Serialize(fixture.CreatePolicy(sequence: 1));
    UpdateTrustTransitionDecision decision = UpdateTrustTransitionEvaluator.Evaluate(
        current,
        proposed,
        [],
        fixture.NowUtc);
    Equal(UpdateTrustTransitionStatus.RejectedRollback, decision.Status);
    return Task.CompletedTask;
}

static Task TrustEquivocationIsRejectedAsync()
{
    using UpdateFixture fixture = new();
    UpdateTrustPolicy currentPolicy = fixture.CreatePolicy();
    UpdateTrustPolicy conflictingPolicy = currentPolicy with
    {
        ValidUntilUtc = currentPolicy.ValidUntilUtc.AddMinutes(1)
    };
    UpdateTrustTransitionDecision decision = UpdateTrustTransitionEvaluator.Evaluate(
        fixture.Serialize(currentPolicy),
        fixture.Serialize(conflictingPolicy),
        [],
        fixture.NowUtc);
    Equal(UpdateTrustTransitionStatus.RejectedEquivocation, decision.Status);
    return Task.CompletedTask;
}

static Task TrustUpgradeIsAcceptedAsync()
{
    using UpdateFixture fixture = new();
    UpdateTrustPolicy currentPolicy = fixture.CreatePolicy();
    UpdateTrustPolicy proposedPolicy = currentPolicy with { Sequence = 2 };
    byte[] proposed = fixture.Serialize(proposedPolicy);
    UpdateTrustTransitionDecision decision = UpdateTrustTransitionEvaluator.Evaluate(
        fixture.Serialize(currentPolicy),
        proposed,
        [fixture.Sign("metadata-1", fixture.MetadataKey, proposed)],
        fixture.NowUtc);
    Equal(UpdateTrustTransitionStatus.AcceptedUpgrade, decision.Status);
    Equal("metadata-1", decision.AuthorizingKeyIds.Single());
    return Task.CompletedTask;
}

static Task TrustKeyReplacementIsRejectedAsync()
{
    using UpdateFixture fixture = new();
    using RSA replacement = RSA.Create(2_048);
    UpdateTrustPolicy currentPolicy = fixture.CreatePolicy();
    UpdateRsaTrustKey replaced = currentPolicy.MetadataKeys[0] with
    {
        PublicKeyPem = replacement.ExportSubjectPublicKeyInfoPem()
    };
    UpdateTrustPolicy proposedPolicy = currentPolicy with
    {
        Sequence = 2,
        MetadataKeys = [replaced]
    };
    byte[] proposed = fixture.Serialize(proposedPolicy);
    UpdateTrustTransitionDecision decision = UpdateTrustTransitionEvaluator.Evaluate(
        fixture.Serialize(currentPolicy),
        proposed,
        [fixture.Sign("metadata-1", fixture.MetadataKey, proposed)],
        fixture.NowUtc);
    Equal(UpdateTrustTransitionStatus.RejectedKeyReplacement, decision.Status);
    return Task.CompletedTask;
}

static async Task ExactAcquisitionLocksAndCleansAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using UpdateFixture fixture = new();
        Dictionary<string, byte[]> payloads = CreatePayloads();
        VerifiedUpdateDescriptor verified = fixture.VerifyDescriptor(
            fixture.CreateDescriptor(payloads));
        ScriptedTransport transport = CreateSuccessfulTransport(verified, payloads);
        BoundedHttpsAcquirer acquirer = new(transport);
        AcquiredUpdateBundle bundle = await acquirer.AcquireAsync(
            verified,
            fixture.CreatePolicy(),
            root,
            fixture.NowUtc);
        string privateRoot = bundle.RootPath;
        try
        {
            Equal(3, bundle.Artifacts.Count);
            foreach ((string name, byte[] expected) in payloads)
            {
                AcquiredUpdateArtifact artifact = bundle.Artifacts[name];
                Equal(Convert.ToHexString(SHA256.HashData(expected)), artifact.Sha256);
                True((await File.ReadAllBytesAsync(artifact.FullPath)).SequenceEqual(expected),
                    $"Acquired bytes changed for {name}.");
                Throws<IOException>(() =>
                {
                    using FileStream writer = new(
                        artifact.FullPath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                });
            }
        }
        finally
        {
            await bundle.DisposeAsync();
        }

        True(!Directory.Exists(privateRoot),
            "Disposing an acquired bundle did not remove private staging.");
    });
}

static async Task UnauthorizedRedirectCleansAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using UpdateFixture fixture = new();
        Dictionary<string, byte[]> payloads = CreatePayloads();
        VerifiedUpdateDescriptor verified = fixture.VerifyDescriptor(
            fixture.CreateDescriptor(payloads));
        ScriptedTransport transport = new();
        Uri manifestUri = verified.ArtifactUris["manifest"];
        transport.Add(
            manifestUri,
            new ResponseSpec(
                HttpStatusCode.Redirect,
                [],
                0,
                UpdateFixture.TlsPin,
                new Uri("https://evil.soltex.invalid/manifest.bin")));
        BoundedHttpsAcquirer acquirer = new(transport);
        await ThrowsAsync<InvalidDataException>(async () =>
        {
            await acquirer.AcquireAsync(
                verified,
                fixture.CreatePolicy(),
                root,
                fixture.NowUtc);
        });
        NoPrivateStaging(root);
    });
}

static async Task TruncatedBodyCleansAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using UpdateFixture fixture = new();
        Dictionary<string, byte[]> payloads = CreatePayloads();
        VerifiedUpdateDescriptor verified = fixture.VerifyDescriptor(
            fixture.CreateDescriptor(payloads));
        ScriptedTransport transport = new();
        Uri manifestUri = verified.ArtifactUris["manifest"];
        transport.Add(
            manifestUri,
            new ResponseSpec(
                HttpStatusCode.OK,
                payloads["manifest"][..^1],
                payloads["manifest"].LongLength,
                UpdateFixture.TlsPin));
        BoundedHttpsAcquirer acquirer = new(transport);
        await ThrowsAsync<InvalidDataException>(async () =>
        {
            await acquirer.AcquireAsync(
                verified,
                fixture.CreatePolicy(),
                root,
                fixture.NowUtc);
        });
        NoPrivateStaging(root);
    });
}

static async Task AcquisitionCancellationCleansAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using UpdateFixture fixture = new();
        Dictionary<string, byte[]> payloads = CreatePayloads();
        VerifiedUpdateDescriptor verified = fixture.VerifyDescriptor(
            fixture.CreateDescriptor(payloads));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        BoundedHttpsAcquirer acquirer = new(new ScriptedTransport());
        await ThrowsAsync<OperationCanceledException>(async () =>
        {
            await acquirer.AcquireAsync(
                verified,
                fixture.CreatePolicy(),
                root,
                fixture.NowUtc,
                cancellation.Token);
        });
        NoPrivateStaging(root);
    });
}

static async Task PlanningJournalSanitizesDetailAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using UpdatePlanningJournal journal = new(Path.Combine(root, "state"));
        await journal.AppendAsync(new UpdatePlanningJournalEntry(
            Guid.NewGuid(),
            UpdatePlanningPhase.Started,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            "  alpha\r\n\tbeta\0  "));
        UpdatePlanningJournalEntry entry = (await journal.ReadAsync()).Single();
        Equal("alpha beta", entry.Detail);
        True(!entry.Detail.Any(char.IsControl),
            "The planning journal persisted control characters.");
    });
}

static async Task PlanningRecoveryCleansPrivateArtifactsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string stagingParent = Path.Combine(root, "staging");
        string token = new('a', 32);
        string privateRoot = UpdatePrivateStaging.CreatePrivateRoot(stagingParent, token);
        await File.WriteAllTextAsync(Path.Combine(privateRoot, "inert.bin"), "inert");
        Guid attempt = Guid.NewGuid();
        using UpdatePlanningJournal journal = new(Path.Combine(root, "state"));
        await journal.AppendAsync(new UpdatePlanningJournalEntry(
            attempt,
            UpdatePlanningPhase.ArtifactsAcquired,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            token,
            "Private inert artifacts acquired."));

        UpdatePlanningRecoveryReport before = await journal.InspectAsync(stagingParent);
        True(before.HasIncompletePlanningAttempt,
            "The incomplete planning attempt was not reported.");
        Equal(token, before.ExistingPrivateStagingTokens.Single());

        await journal.CleanupPlanningArtifactsAsync(attempt, stagingParent);
        True(!Directory.Exists(privateRoot),
            "Planning cleanup left its private staging directory behind.");
        UpdatePlanningRecoveryReport after = await journal.InspectAsync(stagingParent);
        True(!after.HasIncompletePlanningAttempt,
            "A cleaned planning attempt remained incomplete.");
        Equal(0, after.ExistingPrivateStagingTokens.Count);
    });
}

static Task UpdateConfirmationIsExactAsync()
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    UpdateConfirmationChallenge challenge = new(
        new string('A', 64),
        "CONFIRM SOLTEX UPDATE AAAAAAAA",
        now.AddMinutes(5));
    True(challenge.IsSatisfiedBy(
            new string('A', 64),
            "CONFIRM SOLTEX UPDATE AAAAAAAA",
            now),
        "An exact, current confirmation was rejected.");
    True(!challenge.IsSatisfiedBy(
            new string('a', 64),
            "CONFIRM SOLTEX UPDATE AAAAAAAA",
            now),
        "The plan hash comparison was not exact.");
    True(!challenge.IsSatisfiedBy(
            new string('A', 64),
            "confirm soltex update aaaaaaaa",
            now),
        "The confirmation phrase comparison was not exact.");
    True(!challenge.IsSatisfiedBy(
            new string('A', 64),
            "CONFIRM SOLTEX UPDATE AAAAAAAA",
            now.AddMinutes(6)),
        "An expired confirmation remained valid.");
    return Task.CompletedTask;
}

static Dictionary<string, byte[]> CreatePayloads() => new(StringComparer.Ordinal)
{
    ["manifest"] = Encoding.UTF8.GetBytes("signed manifest fixture"),
    ["signature"] = Encoding.UTF8.GetBytes("detached signature fixture"),
    ["package"] = Encoding.UTF8.GetBytes("inert package fixture")
};

static ScriptedTransport CreateSuccessfulTransport(
    VerifiedUpdateDescriptor verified,
    IReadOnlyDictionary<string, byte[]> payloads)
{
    ScriptedTransport transport = new();
    foreach (UpdateArtifactDescriptor artifact in verified.Descriptor.Artifacts)
    {
        byte[] body = payloads[artifact.Name];
        transport.Add(
            verified.ArtifactUris[artifact.Name],
            new ResponseSpec(
                HttpStatusCode.OK,
                body,
                body.LongLength,
                UpdateFixture.TlsPin));
    }

    return transport;
}

static void NoPrivateStaging(string parent)
{
    True(
        !Directory.Exists(parent) ||
        !Directory.EnumerateDirectories(parent, "soltex-update-*", SearchOption.TopDirectoryOnly).Any(),
        "A rejected acquisition left private staging behind.");
}

static async Task WithTempDirectoryAsync(Func<string, Task> action)
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "WaveSlate.Update.Tests",
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

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class UpdateFixture : IDisposable
{
    internal const string Origin = "https://updates.soltex.invalid";
    internal static readonly string TlsPin = new('D', 64);
    private static readonly string PublisherSubject = new('B', 64);
    private static readonly string PublisherSpki = new('C', 64);

    internal UpdateFixture()
    {
        MetadataKey = RSA.Create(2_048);
        MetadataKey2 = RSA.Create(2_048);
        ReleaseKey = RSA.Create(2_048);
    }

    internal DateTimeOffset NowUtc { get; } =
        new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    internal RSA MetadataKey { get; }
    internal RSA MetadataKey2 { get; }
    internal RSA ReleaseKey { get; }

    internal UpdateTrustPolicy CreatePolicy(
        long sequence = 1,
        int quorum = 1,
        bool includeSecondMetadataKey = false)
    {
        DateTimeOffset notBefore = NowUtc.AddDays(-1);
        DateTimeOffset notAfter = NowUtc.AddDays(30);
        List<UpdateRsaTrustKey> metadata =
        [
            new(
                "metadata-1",
                MetadataKey.ExportSubjectPublicKeyInfoPem(),
                UpdateTrustStatus.Active,
                notBefore,
                notAfter,
                null)
        ];
        if (includeSecondMetadataKey)
        {
            metadata.Add(new UpdateRsaTrustKey(
                "metadata-2",
                MetadataKey2.ExportSubjectPublicKeyInfoPem(),
                UpdateTrustStatus.Active,
                notBefore,
                notAfter,
                null));
        }

        return new UpdateTrustPolicy(
            1,
            sequence,
            notBefore,
            notAfter,
            quorum,
            metadata,
            [
                new UpdateRsaTrustKey(
                    "release-1",
                    ReleaseKey.ExportSubjectPublicKeyInfoPem(),
                    UpdateTrustStatus.Active,
                    notBefore,
                    notAfter,
                    null)
            ],
            [
                new UpdateTlsPin(
                    Origin,
                    TlsPin,
                    UpdateTrustStatus.Active,
                    notBefore,
                    notAfter,
                    null)
            ],
            [
                new UpdatePublisherPin(
                    new ApprovedPublisherIdentity(
                        "Soltex update test publisher",
                        PublisherSubject,
                        PublisherSpki),
                    UpdateTrustStatus.Active,
                    notBefore,
                    notAfter,
                    null)
            ]);
    }

    internal UpdateAcquisitionDescriptor CreateDescriptor(
        IReadOnlyDictionary<string, byte[]> payloads) =>
        new(
            1,
            "Soltex",
            "descriptor-fixture-1",
            "stable",
            1,
            "1.0.0",
            NowUtc.AddMinutes(-5),
            NowUtc.AddHours(1),
            "release-1",
            Origin,
            [],
            2,
            10,
            10,
            30,
            [
                Artifact("manifest", payloads["manifest"]),
                Artifact("signature", payloads["signature"]),
                Artifact("package", payloads["package"])
            ]);

    internal byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(
            value,
            StrictJson.CreateSerializerOptions());

    internal UpdateDetachedSignature Sign(string keyId, RSA key, byte[] bytes) =>
        new(
            keyId,
            Convert.ToBase64String(key.SignData(
                bytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss)));

    internal VerifiedUpdateDescriptor VerifyDescriptor(
        UpdateAcquisitionDescriptor descriptor)
    {
        byte[] bytes = Serialize(descriptor);
        UpdateDescriptorVerificationResult result = UpdateDescriptorVerifier.Verify(
            bytes,
            [Sign("metadata-1", MetadataKey, bytes)],
            CreatePolicy(),
            NowUtc);
        return result.Verified ?? throw new InvalidOperationException(result.Detail);
    }

    private static UpdateArtifactDescriptor Artifact(string name, byte[] bytes) =>
        new(
            name,
            $"{Origin}/{name}.bin",
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)));

    public void Dispose()
    {
        MetadataKey.Dispose();
        MetadataKey2.Dispose();
        ReleaseKey.Dispose();
    }
}

sealed record ResponseSpec(
    HttpStatusCode StatusCode,
    byte[] Body,
    long? ContentLength,
    string TlsPin,
    Uri? RedirectLocation = null,
    Uri? EffectiveUri = null);

sealed class ScriptedTransport : IAuthenticatedHttpsTransport
{
    private readonly Dictionary<string, Queue<ResponseSpec>> _responses =
        new(StringComparer.Ordinal);

    internal void Add(Uri uri, ResponseSpec response)
    {
        if (!_responses.TryGetValue(uri.AbsoluteUri, out Queue<ResponseSpec>? queue))
        {
            queue = new Queue<ResponseSpec>();
            _responses.Add(uri.AbsoluteUri, queue);
        }

        queue.Enqueue(response);
    }

    public Task<AuthenticatedHttpsResponse> OpenAsync(
        Uri uri,
        IReadOnlySet<string> acceptedTlsSpkiSha256,
        TimeSpan headerTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_responses.TryGetValue(uri.AbsoluteUri, out Queue<ResponseSpec>? queue) ||
            queue.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response exists for {uri}.");
        }

        ResponseSpec response = queue.Dequeue();
        if (!acceptedTlsSpkiSha256.Contains(response.TlsPin))
        {
            throw new InvalidOperationException(
                "The acquirer did not provide the expected TLS pin set.");
        }

        if (headerTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The acquirer supplied an invalid header timeout.");
        }
        return Task.FromResult(new AuthenticatedHttpsResponse(
            response.StatusCode,
            uri,
            response.EffectiveUri ?? uri,
            response.RedirectLocation,
            response.ContentLength,
            response.TlsPin,
            new MemoryStream(response.Body, writable: false)));
    }
}
