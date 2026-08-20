using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Soltex.Security;

List<(string Name, Func<Task> Test)> tests =
[
    ("Publisher verification binds trust and identity to one immutable snapshot", PublisherVerificationHoldsImmutableLockAsync),
    ("Authenticated envelopes round trip with a last-known-good generation", AuthenticatedEnvelopeRoundTripsAsync),
    ("Legacy data and MAC migrate only after verification", LegacyAuthenticatedStateMigratesAsync),
    ("A torn current envelope recovers from last-known-good state", TornCurrentEnvelopeRecoversAsync),
    ("Unverifiable current and backup envelopes fail closed", UnverifiableEnvelopeSetFailsAsync),
    ("Same-generation authenticated envelope equivocation fails closed", EnvelopeEquivocationFailsAsync),
    ("Authenticated state rejects duplicate JSON properties", AuthenticatedStateRejectsDuplicatePropertiesAsync),
    ("Protected history round trips without plaintext state", ProtectedHistoryRoundTripsWithoutPlaintextAsync),
    ("Protected history expiry rewrites prior generations", ProtectedHistoryExpiryRewritesPriorGenerationsAsync),
    ("Protected history rejects oversized content and entry sets", ProtectedHistoryRejectsOversizedInputAsync),
    ("Protected history corruption fails closed", ProtectedHistoryCorruptionFailsClosedAsync),
    ("Protected history clear removes current and backup artifacts", ProtectedHistoryClearRemovesOwnedArtifactsAsync),
    ("Protected history honors pre-cancelled operations", ProtectedHistoryHonorsCancellationAsync),
    ("Protected provider secret round trips without plaintext generations", ProtectedSecretRoundTripsWithoutPlaintextAsync),
    ("Protected provider secret corruption fails closed and remains deletable", ProtectedSecretCorruptionFailsClosedAsync),
    ("Protected provider secret honors pre-cancelled operations", ProtectedSecretHonorsCancellationAsync),
    ("ZIP preflight rejects excessive central-directory entry counts", ZipPreflightRejectsExcessiveCountsAsync),
    ("ZIP preflight rejects malformed central-directory records", ZipPreflightRejectsMalformedDirectoryAsync),
    ("ZIP preflight rejects truncated archives", ZipPreflightRejectsTruncatedArchiveAsync),
    ("ZIP preflight fails closed on ZIP64 metadata", ZipPreflightRejectsZip64Async),
    ("GitHub workflow dependencies remain immutable and least privilege", WorkflowPinsRemainImmutableAsync)
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

static async Task PublisherVerificationHoldsImmutableLockAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        string dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
        string signedHost = Path.Combine(dotnetRoot, "dotnet.exe");
        string testPath = Path.Combine(root, "signed-host.exe");
        File.Copy(signedHost, testPath);

        AuthenticodeSignerIdentityResult identity = AuthenticodeSignerIdentityReader.Read(testPath);
        PublisherIdentityEvidence evidence = identity.Evidence
            ?? throw new InvalidOperationException(identity.Detail);
        PublisherPolicy policy = new(
        [
            new ApprovedPublisherIdentity(
                "Observed immutable .NET host signer",
                evidence.SubjectNameSha256,
                evidence.SubjectPublicKeyInfoSha256)
        ]);

        await using LockedPublisherFile locked = await LockedPublisherFile.OpenAsync(
            testPath,
            CancellationToken.None);
        string exactSnapshotHash = locked.Sha256;
        True(
            !string.Equals(locked.OriginalPath, locked.FullPath, StringComparison.OrdinalIgnoreCase),
            "Publisher verification did not create an independent immutable snapshot.");

        await File.WriteAllBytesAsync(testPath, [0x4D, 0x5A, 0x00, 0x00]);
        Throws<IOException>(() =>
        {
            using FileStream writer = new(
                locked.FullPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        });
        Throws<IOException>(() => File.Delete(locked.FullPath));

        AuthenticodePublisherVerificationResult result =
            await AuthenticodePublisherVerifier.VerifyLockedAsync(
                locked,
                policy,
                allowNetworkRevocationRetrieval: false,
                CancellationToken.None);
        True(result.IsApproved, result.Detail);
        Equal(exactSnapshotHash, result.FileSha256);
        True(
            await locked.VerifyHashUnchangedAsync(CancellationToken.None),
            "The immutable publisher snapshot changed during trust and signer evaluation.");
        True(
            !string.Equals(
                await FileHashing.Sha256Async(testPath),
                result.FileSha256,
                StringComparison.Ordinal),
            "Publisher approval was accidentally rebound to the changed source path.");
    });
}

static async Task AuthenticatedEnvelopeRoundTripsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using AuthenticatedJsonStore store = new(root, "fixture");
        await store.SaveAsync(new FixtureState(1, "alpha"));
        await store.SaveAsync(new FixtureState(2, "beta"));
        FixtureState loaded = await store.LoadAsync(static () => new FixtureState(0, "default"));
        Equal(new FixtureState(2, "beta"), loaded);
        True(File.Exists(store.StatePath), "The authenticated envelope was not written.");
        True(File.Exists(store.BackupPath), "The last-known-good envelope was not written.");
        True(
            !File.Exists(Path.Combine(root, "fixture.mac")),
            "New authenticated state must not use a separately committed MAC file.");
    });
}

static async Task LegacyAuthenticatedStateMigratesAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        byte[] key;
        using (AuthenticatedJsonStore keyStore = new(root, "legacy"))
        {
            key = await keyStore.GetKeyCopyAsync();
        }

        FixtureState expected = new(7, "verified legacy");
        byte[] legacyJson = JsonSerializer.SerializeToUtf8Bytes(
            expected,
            StrictJson.CreateSerializerOptions(writeIndented: true));
        await File.WriteAllBytesAsync(Path.Combine(root, "legacy.json"), legacyJson);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "legacy.mac"),
            HMACSHA256.HashData(key, legacyJson));
        CryptographicOperations.ZeroMemory(key);

        using AuthenticatedJsonStore migrated = new(root, "legacy");
        FixtureState actual = await migrated.LoadAsync(static () => new FixtureState(0, "default"));
        Equal(expected, actual);
        byte[] prefix = (await File.ReadAllBytesAsync(migrated.StatePath))[..8];
        Equal("SLTXST01", Encoding.ASCII.GetString(prefix));
        True(File.Exists(migrated.BackupPath), "Migration did not establish last-known-good state.");
        True(!File.Exists(Path.Combine(root, "legacy.mac")), "The verified legacy MAC remained authoritative.");
    });
}

static async Task TornCurrentEnvelopeRecoversAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using AuthenticatedJsonStore store = new(root, "recover");
        await store.SaveAsync(new FixtureState(1, "last good"));
        await store.SaveAsync(new FixtureState(2, "new current"));
        await File.WriteAllBytesAsync(store.StatePath, [0x53, 0x4C, 0x54]);

        FixtureState recovered = await store.LoadAsync(static () => new FixtureState(0, "default"));
        Equal(new FixtureState(1, "last good"), recovered);
        using AuthenticatedJsonStore reopened = new(root, "recover");
        Equal(
            new FixtureState(1, "last good"),
            await reopened.LoadAsync(static () => new FixtureState(0, "default")));
    });
}

static async Task UnverifiableEnvelopeSetFailsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using AuthenticatedJsonStore store = new(root, "tamper");
        await store.SaveAsync(new FixtureState(1, "first"));
        await store.SaveAsync(new FixtureState(2, "second"));
        await File.AppendAllTextAsync(store.StatePath, "tampered");
        await File.AppendAllTextAsync(store.BackupPath, "tampered");
        await ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(static () => new FixtureState(0, "default")));
    });
}

static async Task EnvelopeEquivocationFailsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using AuthenticatedJsonStore store = new(root, "equivocation");
        byte[] key = await store.GetKeyCopyAsync();
        try
        {
            byte[] firstPayload = JsonSerializer.SerializeToUtf8Bytes(
                new FixtureState(1, "A"),
                StrictJson.CreateSerializerOptions());
            byte[] secondPayload = JsonSerializer.SerializeToUtf8Bytes(
                new FixtureState(1, "B"),
                StrictJson.CreateSerializerOptions());
            await File.WriteAllBytesAsync(
                store.StatePath,
                store.CreateEnvelopeForTesting(9, firstPayload, key));
            await File.WriteAllBytesAsync(
                store.BackupPath,
                store.CreateEnvelopeForTesting(9, secondPayload, key));
            await ThrowsAsync<InvalidDataException>(
                () => store.LoadAsync(static () => new FixtureState(0, "default")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    });
}

static async Task AuthenticatedStateRejectsDuplicatePropertiesAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using AuthenticatedJsonStore store = new(root, "duplicate");
        byte[] key = await store.GetKeyCopyAsync();
        try
        {
            byte[] duplicatePayload = Encoding.UTF8.GetBytes(
                "{\"sequence\":1,\"sequence\":2,\"value\":\"duplicate\"}");
            await File.WriteAllBytesAsync(
                store.StatePath,
                store.CreateEnvelopeForTesting(1, duplicatePayload, key));
            await ThrowsAsync<InvalidDataException>(
                () => store.LoadAsync(static () => new FixtureState(0, "default")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    });
}

static async Task ProtectedHistoryRoundTripsWithoutPlaintextAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        const string retainedText = "private dictation fixture";
        await using AuthenticatedProtectedHistoryStore store = new(root, "history-roundtrip");
        ProtectedHistoryRecord expected = new(
            DateTimeOffset.UtcNow,
            "notepad",
            "Insert",
            retainedText);
        await store.SaveAsync([expected], retentionDays: 7);

        IReadOnlyList<ProtectedHistoryRecord> actual = await store.LoadAsync(7);
        Equal(1, actual.Count);
        Equal(expected, actual[0]);

        byte[] needle = Encoding.UTF8.GetBytes(retainedText);
        foreach (string path in new[]
                 {
                     Path.Combine(root, "history-roundtrip.json"),
                     Path.Combine(root, "history-roundtrip.json.bak")
                 })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            byte[] state = await File.ReadAllBytesAsync(path);
            True(
                !ContainsSequence(state, needle),
                "Authenticated protected history exposed retained text in its state envelope.");
        }

        CryptographicOperations.ZeroMemory(needle);
    });
}

static async Task ProtectedHistoryExpiryRewritesPriorGenerationsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        DateTimeOffset start = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        MutableTimeProvider time = new(start);
        await using AuthenticatedProtectedHistoryStore store = new(
            root,
            "history-expiry",
            time);
        await store.SaveAsync(
            [new ProtectedHistoryRecord(start, "notepad", "Insert", "expiry fixture")],
            retentionDays: 7);
        time.UtcNow = start.AddDays(8);

        IReadOnlyList<ProtectedHistoryRecord> loaded = await store.LoadAsync(7);
        Equal(0, loaded.Count);
        True(
            !File.Exists(Path.Combine(root, "history-expiry.json")),
            "Expired protected history left its current generation behind.");
        True(
            !File.Exists(Path.Combine(root, "history-expiry.json.bak")),
            "Expired protected history left its backup generation behind.");
    });
}

static async Task ProtectedHistoryRejectsOversizedInputAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        await using AuthenticatedProtectedHistoryStore store = new(root, "history-bounds");
        ProtectedHistoryRecord oversized = new(
            DateTimeOffset.UtcNow,
            "notepad",
            "Insert",
            new string('x', AuthenticatedProtectedHistoryStore.MaximumTextCharacters + 1));
        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SaveAsync([oversized], 7).AsTask());

        ProtectedHistoryRecord[] tooMany = Enumerable
            .Range(0, AuthenticatedProtectedHistoryStore.MaximumEntries + 1)
            .Select(_ => new ProtectedHistoryRecord(
                DateTimeOffset.UtcNow,
                "notepad",
                "Insert",
                "bounded"))
            .ToArray();
        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SaveAsync(tooMany, 7).AsTask());
    });
}

static async Task ProtectedHistoryCorruptionFailsClosedAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        await using AuthenticatedProtectedHistoryStore store = new(root, "history-corrupt");
        ProtectedHistoryRecord record = new(
            DateTimeOffset.UtcNow,
            "notepad",
            "Insert",
            "corruption fixture");
        await store.SaveAsync([record], 7);
        await store.SaveAsync([record with { CreatedAtUtc = DateTimeOffset.UtcNow }], 7);
        await File.AppendAllTextAsync(Path.Combine(root, "history-corrupt.json"), "changed");
        await File.AppendAllTextAsync(Path.Combine(root, "history-corrupt.json.bak"), "changed");

        await ThrowsAsync<InvalidDataException>(() => store.LoadAsync(7).AsTask());
    });
}

static async Task ProtectedHistoryClearRemovesOwnedArtifactsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        await using AuthenticatedProtectedHistoryStore store = new(root, "history-clear");
        ProtectedHistoryRecord record = new(
            DateTimeOffset.UtcNow,
            "notepad",
            "Insert",
            "clear fixture");
        await store.SaveAsync([record], 7);
        await store.SaveAsync([record], 7);
        await store.ClearAsync();

        True(
            !File.Exists(Path.Combine(root, "history-clear.json")),
            "Protected history current state remained after clear.");
        True(
            !File.Exists(Path.Combine(root, "history-clear.json.bak")),
            "Protected history backup state remained after clear.");
        True(
            File.Exists(Path.Combine(root, "state.key")),
            "Clearing one protected store removed the shared authenticated-state key.");
        Equal(0, (await store.LoadAsync(7)).Count);
    });
}

static async Task ProtectedHistoryHonorsCancellationAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        await using AuthenticatedProtectedHistoryStore store = new(root, "history-cancel");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(
            () => store.LoadAsync(7, cancellation.Token).AsTask());
        await ThrowsAsync<OperationCanceledException>(
            () => store.SaveAsync(
                [new ProtectedHistoryRecord(
                    DateTimeOffset.UtcNow,
                    "notepad",
                    "Insert",
                    "cancel fixture")],
                7,
                cancellation.Token).AsTask());
    });
}

static async Task ProtectedSecretRoundTripsWithoutPlaintextAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        byte[] first = "first-owner-provider-secret"u8.ToArray();
        byte[] second = "rotated-owner-provider-secret"u8.ToArray();
        try
        {
            await using AuthenticatedProtectedSecretStore store = new(
                root,
                "provider-secret");
            True(!await store.IsAvailableAsync(), "A missing secret was reported as available.");

            await store.SaveAsync(first);
            True(await store.IsAvailableAsync(), "A saved secret was reported as unavailable.");
            ProtectedSecretLease lease = await store.AcquireAsync()
                ?? throw new InvalidOperationException("Saved protected secret was unavailable.");
            ReadOnlyMemory<byte> observed = lease.Bytes;
            True(observed.Span.SequenceEqual(first), "Protected secret round-trip changed bytes.");
            lease.Dispose();
            True(lease.IsDisposed, "Protected secret lease did not enter the disposed state.");
            True(
                observed.Span.ToArray().All(value => value == 0),
                "Protected secret lease did not clear its owned byte array.");

            await store.SaveAsync(second);
            True(
                !File.Exists(store.BackupPath),
                "Credential rotation retained a last-known-good generation of the old secret.");
            foreach (string path in Directory.EnumerateFiles(root))
            {
                byte[] state = await File.ReadAllBytesAsync(path);
                True(
                    !ContainsSequence(state, first) && !ContainsSequence(state, second),
                    "Protected provider state exposed clear credential bytes.");
            }

            await store.DeleteAsync();
            True(!File.Exists(store.StatePath), "Protected provider state remained after deletion.");
            True(!File.Exists(store.BackupPath), "Protected provider backup remained after deletion.");
            True(
                File.Exists(Path.Combine(root, "state.key")),
                "Deleting one provider secret removed the shared authenticated-state key.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(second);
        }
    });
}

static async Task ProtectedSecretCorruptionFailsClosedAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        byte[] secret = "corruption-fixture-secret"u8.ToArray();
        try
        {
            await using AuthenticatedProtectedSecretStore store = new(
                root,
                "provider-corrupt");
            await store.SaveAsync(secret);
            await File.AppendAllTextAsync(store.StatePath, "changed");
            await ThrowsAsync<InvalidDataException>(() => store.AcquireAsync().AsTask());

            await store.DeleteAsync();
            True(
                !File.Exists(store.StatePath) && !File.Exists(store.BackupPath),
                "A corrupted protected secret could not be removed by its owner.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    });
}

static async Task ProtectedSecretHonorsCancellationAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        byte[] secret = "cancelled-provider-secret"u8.ToArray();
        try
        {
            await using AuthenticatedProtectedSecretStore store = new(
                root,
                "provider-cancel");
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(
                () => store.SaveAsync(secret, cancellation.Token).AsTask());
            await ThrowsAsync<OperationCanceledException>(
                () => store.AcquireAsync(cancellation.Token).AsTask());
            True(
                !File.Exists(store.StatePath),
                "A pre-cancelled credential operation created provider state.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    });
}

static async Task ZipPreflightRejectsExcessiveCountsAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "too-many.zip");
        WriteEndRecordOnly(archivePath, totalEntries: 9);
        BoundedArchiveStager stager = new(new ArchiveStagingLimits(MaximumEntries: 8));
        string staging = Path.Combine(root, "staging");
        await ThrowsAsync<InvalidDataException>(() => stager.StageZipAsync(archivePath, staging));
        True(!Directory.Exists(staging), "Entry-count rejection allocated a staging directory.");
    });
}

static async Task ZipPreflightRejectsMalformedDirectoryAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "malformed-directory.zip");
        byte[] centralHeader = new byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(centralHeader, 0x02014B50);
        BinaryPrimitives.WriteUInt16LittleEndian(centralHeader.AsSpan(28, 2), 10);
        using (FileStream stream = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(centralHeader);
            WriteEndRecord(stream, totalEntries: 1, centralDirectoryLength: 46, centralDirectoryOffset: 0);
        }

        BoundedArchiveStager stager = new();
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, Path.Combine(root, "staging")));
    });
}

static async Task ZipPreflightRejectsTruncatedArchiveAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "truncated.zip");
        await File.WriteAllBytesAsync(archivePath, new byte[21]);
        BoundedArchiveStager stager = new();
        await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, Path.Combine(root, "staging")));
    });
}

static async Task ZipPreflightRejectsZip64Async()
{
    await WithTempDirectoryAsync(async root =>
    {
        string archivePath = Path.Combine(root, "zip64.zip");
        WriteEndRecordOnly(archivePath, totalEntries: ushort.MaxValue);
        BoundedArchiveStager stager = new();
        InvalidDataException exception = await ThrowsAsync<InvalidDataException>(
            () => stager.StageZipAsync(archivePath, Path.Combine(root, "staging")));
        True(
            exception.Message.Contains("ZIP64", StringComparison.OrdinalIgnoreCase),
            "ZIP64 rejection was not explicit.");
    });
}

static Task WorkflowPinsRemainImmutableAsync()
{
    string repositoryRoot = FindRepositoryRoot();
    string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "windows.yml");
    string workflow = File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);
    True(
        workflow.Contains("permissions:\n  contents: read\n", StringComparison.Ordinal),
        "The workflow must retain top-level contents: read permissions.");
    True(
        !workflow.Contains("pull_request_target:", StringComparison.Ordinal),
        "The untrusted-code workflow must not use pull_request_target.");

    string[] forbiddenWritePermissions =
    [
        "contents: write",
        "actions: write",
        "checks: write",
        "deployments: write",
        "id-token: write",
        "packages: write",
        "pull-requests: write",
        "security-events: write",
        "statuses: write"
    ];
    foreach (string forbidden in forbiddenWritePermissions)
    {
        True(
            !workflow.Split('\n').Any(line =>
                string.Equals(line.Trim(), forbidden, StringComparison.OrdinalIgnoreCase)),
            $"The workflow must not grant '{forbidden}'.");
    }

    Dictionary<string, string> expected = new(StringComparer.Ordinal)
    {
        ["actions/checkout"] = "de0fac2e4500dabe0009e67214ff5f5447ce83dd",
        ["actions/setup-dotnet"] = "26b0ec14cb23fa6904739307f278c14f94c95bf1",
        ["actions/upload-artifact"] = "b7c566a772e6b6bfb58ed0dc250532a479d7789f"
    };
    int actionCount = 0;
    foreach (string line in workflow.Split('\n'))
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith("- uses:", StringComparison.Ordinal))
        {
            continue;
        }

        actionCount++;
        string actionReference = trimmed["- uses:".Length..].Trim();
        int commentIndex = actionReference.IndexOf('#');
        if (commentIndex >= 0)
        {
            actionReference = actionReference[..commentIndex].TrimEnd();
        }

        int separator = actionReference.LastIndexOf('@');
        True(separator > 0, "A workflow action is missing an immutable ref separator.");
        string action = actionReference[..separator];
        string sha = actionReference[(separator + 1)..];
        True(
            sha.Length == 40 && sha.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            $"Workflow action '{action}' is not pinned to a full lowercase commit SHA.");
        if (!expected.Remove(action, out string? expectedSha) || expectedSha is null)
        {
            throw new InvalidOperationException($"Unexpected workflow action: {action}");
        }

        Equal(expectedSha, sha);
    }

    Equal(3, actionCount);
    Equal(0, expected.Count);
    True(
        workflow.Split('\n').Any(line =>
            string.Equals(
                line.Trim(),
                "persist-credentials: false",
                StringComparison.Ordinal)),
        "Checkout must not persist GitHub credentials.");
    return Task.CompletedTask;
}

static void WriteEndRecordOnly(string path, ushort totalEntries)
{
    using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    WriteEndRecord(stream, totalEntries, centralDirectoryLength: 0, centralDirectoryOffset: 0);
}

static void WriteEndRecord(
    Stream stream,
    ushort totalEntries,
    uint centralDirectoryLength,
    uint centralDirectoryOffset)
{
    byte[] record = new byte[22];
    BinaryPrimitives.WriteUInt32LittleEndian(record, 0x06054B50);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), totalEntries);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10, 2), totalEntries);
    BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12, 4), centralDirectoryLength);
    BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16, 4), centralDirectoryOffset);
    stream.Write(record);
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, ".github", "workflows", "windows.yml")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("The repository root could not be located.");
}

static async Task WithTempDirectoryAsync(Func<string, Task> action)
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "Soltex.Security.Hardening.Tests",
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

static bool ContainsSequence(byte[] haystack, byte[] needle)
{
    ArgumentNullException.ThrowIfNull(haystack);
    ArgumentNullException.ThrowIfNull(needle);
    if (needle.Length == 0)
    {
        return true;
    }

    for (int index = 0; index <= haystack.Length - needle.Length; index++)
    {
        if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
        {
            return true;
        }
    }

    return false;
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

sealed record FixtureState(int Sequence, string Value);

sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
