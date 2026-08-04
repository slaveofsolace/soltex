using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaveSlate.Security;

List<(string Name, Func<Task> Test)> tests =
[
    ("Publisher verification binds trust and identity to one immutable snapshot", PublisherVerificationHoldsImmutableLockAsync),
    ("Authenticated envelopes round trip with a last-known-good generation", AuthenticatedEnvelopeRoundTripsAsync),
    ("Legacy data and MAC migrate only after verification", LegacyAuthenticatedStateMigratesAsync),
    ("A torn current envelope recovers from last-known-good state", TornCurrentEnvelopeRecoversAsync),
    ("Unverifiable current and backup envelopes fail closed", UnverifiableEnvelopeSetFailsAsync),
    ("Same-generation authenticated envelope equivocation fails closed", EnvelopeEquivocationFailsAsync),
    ("Authenticated state rejects duplicate JSON properties", AuthenticatedStateRejectsDuplicatePropertiesAsync),
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
        "WaveSlate.Security.Hardening.Tests",
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

sealed record FixtureState(int Sequence, string Value);
