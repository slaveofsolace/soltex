using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Soltex.RemoteAssist;
using Soltex.Security;

string? childMode = Environment.GetEnvironmentVariable("SOLTEX_TEST_CHILD_MODE");
if (string.Equals(childMode, "oversized-output", StringComparison.Ordinal))
{
    Console.Out.Write(new string('X', 32 * 1024));
    return 0;
}

if (string.Equals(childMode, "report-working-directory", StringComparison.Ordinal))
{
    Console.Out.Write(JsonSerializer.Serialize(new { AMRunningMode = Environment.CurrentDirectory }));
    return 0;
}

if (string.Equals(childMode, "sensitive-error", StringComparison.Ordinal))
{
    Console.Error.Write(@"C:\Users\example\private\sample.exe");
    return 23;
}

if (string.Equals(childMode, "oversized-event-envelope", StringComparison.Ordinal))
{
    const string eventXml = "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><EventData /></Event>";
    DateTimeOffset timestamp = DateTimeOffset.UtcNow;
    Console.Out.Write(JsonSerializer.Serialize(new
    {
        Items = new[]
        {
            new { EventId = 1000, RecordId = 1L, TimeCreatedUtc = timestamp, Xml = eventXml },
            new { EventId = 1001, RecordId = 2L, TimeCreatedUtc = timestamp, Xml = eventXml }
        }
    }));
    return 0;
}

List<(string Name, Func<Task> Test)> tests =
[
    ("SHA-256 hashing is deterministic", HashingIsDeterministicAsync),
    ("Manifest paths cannot escape their root", PathsCannotEscapeAsync),
    ("Allow-list state detects tampering", AllowListDetectsTamperingAsync),
    ("Quarantine round trip preserves bytes", QuarantineRoundTripAsync),
    ("Signed integrity manifest detects content changes", IntegrityManifestDetectsChangesAsync),
    ("Integrity manifest identity compatibility is versioned", IntegrityManifestIdentityIsVersionedAsync),
    ("Fresh profiles select the canonical Soltex data root", FreshProfileUsesCanonicalDataRootAsync),
    ("Existing profiles select the sole legacy data root", ExistingProfileUsesLegacyDataRootAsync),
    ("Ambiguous or unsafe product data roots fail closed", AmbiguousOrUnsafeDataRootsFailClosedAsync),
    ("Audit log detects mutation", AuditLogDetectsMutationAsync),
    ("Authenticode trusts the signed .NET host", AuthenticodeTrustsDotNetHostAsync),
    ("Authenticode rejects an unsigned Soltex assembly", AuthenticodeRejectsUnsignedAssemblyAsync),
    ("AMSI accepts a benign fixture", AmsiAcceptsBenignFixtureAsync),
    ("Defender event parser redacts resource paths", DefenderEventParserRedactsPathsAsync),
    ("Protection monitor backs off and reports recovery", ProtectionMonitorRecoversAsync),
    ("Protection monitor isolates provider and subscriber faults", ProtectionMonitorIsolatesFaultsAsync),
    ("Protection monitor shuts down during a pending wake", ProtectionMonitorShutdownRaceAsync),
    ("Protection monitor does not abandon timed-out refresh waits", ProtectionMonitorRefreshAfterPollsAsync),
    ("Provider-neutral health preserves Windows Security Center precedence", ProviderNeutralHealthPrecedenceAsync),
    ("Native security imports resolve only from System32", NativeImportsUseSystem32Async),
    ("PowerShell security commands pin system module manifests", PowerShellModulesArePinnedAsync),
    ("Windows Security change registration is bounded", WindowsSecurityChangeRegistrationAsync),
    ("Windows protection health query is bounded", DefenderHealthQueryAsync),
    ("Defender Operational event query is bounded", DefenderEventQueryAsync),
    ("Defender command output is byte-bounded", DefenderOutputIsBoundedAsync),
    ("PowerShell child boundary anchors its directory and redacts errors", PowerShellChildBoundaryIsHardenedAsync),
    ("Defender event envelopes enforce the requested count", DefenderEventEnvelopeIsBoundedAsync),
    ("Security observation overhead is measured", SecurityObservationPerformanceAsync),
    ("Remote Assist peer IDs reject argument injection", RemotePeerIdsAreConstrainedAsync),
    ("Remote Assist launch plans are fixed and shell-free", RemoteLaunchPlansAreFixedAsync),
    ("Remote Assist executable discovery is bounded", RemoteExecutableDiscoveryIsBoundedAsync)
];

if (string.Equals(
        Environment.GetEnvironmentVariable(ProductIdentity.RunEicarEnvironmentVariable),
        "1",
        StringComparison.Ordinal) ||
    string.Equals(
        Environment.GetEnvironmentVariable(ProductIdentity.LegacyRunEicarEnvironmentVariable),
        "1",
        StringComparison.Ordinal))
{
    tests.Add(("AMSI detects the safe EICAR test marker", AmsiDetectsEicarAsync));
}

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

static async Task HashingIsDeterministicAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string path = Path.Combine(root, "fixture.txt");
        await File.WriteAllTextAsync(path, "Soltex");
        string actual = await FileHashing.Sha256Async(path);
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Soltex")));
        Equal(expected, actual);
    });
}

static Task PathsCannotEscapeAsync()
{
    string root = Path.Combine(Path.GetTempPath(), "soltex-root");
    Throws<InvalidDataException>(() => PathSafety.CombineUnderRoot(root, "..\\outside.dll"));
    return Task.CompletedTask;
}

static async Task AllowListDetectsTamperingAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        using SecurityRuntime runtime = SecurityRuntime.CreateDefault(Path.Combine(root, "runtime"));
        string hash = new('A', 64);
        await runtime.AllowList.AddAsync(hash, "Fixture", "Test approval");
        True(await runtime.AllowList.ContainsAsync(hash), "Expected allow-list entry.");

        string statePath = Path.Combine(root, "runtime", "state", "allow-list.json");
        await File.AppendAllTextAsync(statePath, " ");
        await ThrowsAsync<InvalidDataException>(() => runtime.AllowList.ListAsync());
    });
}

static async Task QuarantineRoundTripAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        string source = Path.Combine(sourceRoot, "sample.bin");
        byte[] fixture = RandomNumberGenerator.GetBytes(4_096);
        await File.WriteAllBytesAsync(source, fixture);

        using SecurityRuntime runtime = SecurityRuntime.CreateDefault(Path.Combine(root, "runtime"));
        QuarantineEntry entry = await runtime.Quarantine.QuarantineAsync(source, "Test-only isolation");
        True(!File.Exists(source), "Source should have moved into quarantine.");
        Equal(1, (await runtime.Quarantine.ListAsync()).Count);

        string restored = await runtime.Quarantine.RestoreAsync(entry.Id);
        SequenceEqual(fixture, await File.ReadAllBytesAsync(restored));
        Equal(0, (await runtime.Quarantine.ListAsync()).Count);
    });
}

static async Task IntegrityManifestDetectsChangesAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string contentRoot = Path.Combine(root, "content");
        Directory.CreateDirectory(contentRoot);
        string contentPath = Path.Combine(contentRoot, "plugin.dll");
        byte[] content = Encoding.UTF8.GetBytes("benign fixture");
        await File.WriteAllBytesAsync(contentPath, content);

        IntegrityManifest manifest = new(
            1,
            ProductIdentity.IntegrityManifestProduct,
            DateTimeOffset.UtcNow,
            [new IntegrityManifestFile("plugin.dll", content.Length, await FileHashing.Sha256Async(contentPath))]);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        string manifestPath = Path.Combine(root, "integrity.json");
        string signaturePath = Path.Combine(root, "integrity.sig");
        await File.WriteAllBytesAsync(manifestPath, manifestBytes);

        using RSA rsa = RSA.Create(2_048);
        byte[] signature = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        await File.WriteAllBytesAsync(signaturePath, signature);
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        IntegrityVerificationResult valid = await IntegrityManifestVerifier.VerifyAsync(
            contentRoot,
            manifestPath,
            signaturePath,
            publicKey);
        True(valid.Succeeded, string.Join("; ", valid.Errors));
        Equal(1, valid.VerifiedFileCount);

        await File.AppendAllTextAsync(contentPath, "tampered");
        IntegrityVerificationResult invalid = await IntegrityManifestVerifier.VerifyAsync(
            contentRoot,
            manifestPath,
            signaturePath,
            publicKey);
        True(!invalid.Succeeded, "Changed content should fail integrity verification.");
    });
}

static async Task IntegrityManifestIdentityIsVersionedAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string contentRoot = Path.Combine(root, "content");
        Directory.CreateDirectory(contentRoot);
        string contentPath = Path.Combine(contentRoot, "plugin.dll");
        byte[] content = Encoding.UTF8.GetBytes("identity compatibility fixture");
        await File.WriteAllBytesAsync(contentPath, content);

        using RSA rsa = RSA.Create(2_048);
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        foreach ((string product, bool accepted) in new[]
                 {
                     (ProductIdentity.IntegrityManifestProduct, true),
                     (ProductIdentity.LegacyIntegrityManifestProduct, true),
                     ("UnrecognizedProduct", false)
                 })
        {
            IntegrityManifest manifest = new(
                1,
                product,
                DateTimeOffset.UtcNow,
                [new IntegrityManifestFile(
                    "plugin.dll",
                    content.Length,
                    await FileHashing.Sha256Async(contentPath))]);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            string token = accepted ? product : "unsupported";
            string manifestPath = Path.Combine(root, token + ".json");
            string signaturePath = Path.Combine(root, token + ".sig");
            await File.WriteAllBytesAsync(manifestPath, manifestBytes);
            await File.WriteAllBytesAsync(
                signaturePath,
                rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

            IntegrityVerificationResult result = await IntegrityManifestVerifier.VerifyAsync(
                contentRoot,
                manifestPath,
                signaturePath,
                publicKey);
            Equal(accepted, result.Succeeded);
        }
    });
}

static async Task FreshProfileUsesCanonicalDataRootAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        ProductDataRootResolution resolution = ProductDataRootResolver.Resolve(root);
        Equal(ProductDataRootKind.Canonical, resolution.Kind);
        Equal(Path.Combine(root, ProductIdentity.CanonicalStorageDirectoryName), resolution.ProductRoot);
        True(Directory.Exists(resolution.ProductRoot), "The canonical product root was not created.");
        True(
            !Directory.Exists(Path.Combine(root, ProductIdentity.LegacyStorageDirectoryName)),
            "A fresh profile must not create the legacy root.");
        return Task.CompletedTask;
    });
}

static async Task ExistingProfileUsesLegacyDataRootAsync()
{
    await WithTempDirectoryAsync(root =>
    {
        string legacyRoot = Path.Combine(root, ProductIdentity.LegacyStorageDirectoryName);
        Directory.CreateDirectory(legacyRoot);
        ProductDataRootResolution resolution = ProductDataRootResolver.Resolve(root);
        Equal(ProductDataRootKind.LegacyCompatibility, resolution.Kind);
        Equal(legacyRoot, resolution.ProductRoot);
        True(resolution.UsesLegacyCompatibility, "Legacy selection must be explicit in diagnostics.");
        True(
            !Directory.Exists(Path.Combine(root, ProductIdentity.CanonicalStorageDirectoryName)),
            "Compatibility lookup must not silently create or copy a canonical root.");
        return Task.CompletedTask;
    });
}

static async Task AmbiguousOrUnsafeDataRootsFailClosedAsync()
{
    await WithTempDirectoryAsync(root =>
    {
        Directory.CreateDirectory(Path.Combine(root, ProductIdentity.CanonicalStorageDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, ProductIdentity.LegacyStorageDirectoryName));
        Throws<InvalidDataException>(() => ProductDataRootResolver.Resolve(root));
        return Task.CompletedTask;
    });

    await WithTempDirectoryAsync(async root =>
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, ProductIdentity.CanonicalStorageDirectoryName),
            "not a directory");
        Throws<InvalidDataException>(() => ProductDataRootResolver.Resolve(root));
    });

    await WithTempDirectoryAsync(root =>
    {
        string target = Path.Combine(root, "target");
        string productLink = Path.Combine(root, ProductIdentity.CanonicalStorageDirectoryName);
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(productLink, target);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            IOException or
            PlatformNotSupportedException)
        {
            Console.WriteLine($"      reparse fixture unavailable: {exception.GetType().Name}");
            return Task.CompletedTask;
        }

        Throws<IOException>(() => ProductDataRootResolver.Resolve(root));
        return Task.CompletedTask;
    });
}

static async Task AuditLogDetectsMutationAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string runtimeRoot = Path.Combine(root, "runtime");
        using SecurityRuntime runtime = SecurityRuntime.CreateDefault(runtimeRoot);
        await runtime.AuditLog.AppendAsync(
            "test.event",
            SecurityEventSeverity.Information,
            "Benign test event",
            Path.Combine(root, "private-path.txt"));
        True(await runtime.AuditLog.VerifyAsync(), "Fresh audit log should verify.");

        string logPath = Path.Combine(runtimeRoot, "events.jsonl");
        string log = await File.ReadAllTextAsync(logPath);
        await File.WriteAllTextAsync(logPath, log.Replace("Benign", "Changed", StringComparison.Ordinal));
        True(!await runtime.AuditLog.VerifyAsync(), "Modified audit log should fail verification.");
    });
}

static Task AuthenticodeTrustsDotNetHostAsync()
{
    string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
    string dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
    string signedHostPath = Path.Combine(dotnetRoot, "dotnet.exe");
    AuthenticodeVerificationResult result = AuthenticodeVerifier.Verify(signedHostPath);
    True(result.IsTrusted, $"Expected trusted .NET host, got {result.Status}: {result.Detail}");
    return Task.CompletedTask;
}

static Task AuthenticodeRejectsUnsignedAssemblyAsync()
{
    string assemblyPath = typeof(ContentVerdict).Assembly.Location;
    AuthenticodeVerificationResult result = AuthenticodeVerifier.Verify(assemblyPath);
    True(!result.IsTrusted, "An unsigned development assembly must not be trusted as a release artifact.");
    return Task.CompletedTask;
}

static Task AmsiAcceptsBenignFixtureAsync()
{
    using IContentScanner scanner = AmsiContentScanner.CreateOrUnavailable();
    ContentScanResult result = scanner.Scan(Encoding.UTF8.GetBytes("Soltex benign fixture"), "benign.txt");
    True(!result.ShouldBlock, $"Unexpected AMSI block: {result.Detail}");
    return Task.CompletedTask;
}

static Task DefenderEventParserRedactsPathsAsync()
{
    const string privatePath = @"C:\Users\example\private\sample.exe";
    string xml = $"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <EventData>
            <Data Name="Threat Name">Demo:SafeFixture</Data>
            <Data Name="Severity Name">Severe</Data>
            <Data Name="Category Name">Test</Data>
            <Data Name="Source Name">AMSI</Data>
            <Data Name="Path">{privatePath}</Data>
          </EventData>
        </Event>
        """;
    DefenderOperationalEvent parsed = DefenderEventLogParser.Parse(
        1116,
        42,
        DateTimeOffset.UtcNow,
        xml);
    Equal(DefenderEventKind.Detection, parsed.Kind);
    Equal(SecurityEventSeverity.Critical, parsed.Severity);
    True(parsed.ResourcePathRedacted, "The parser should report that a resource path was redacted.");
    True(!parsed.Detail.Contains(privatePath, StringComparison.OrdinalIgnoreCase), "Raw paths must not reach event details.");
    True(!parsed.Detail.Contains("private", StringComparison.OrdinalIgnoreCase), "Path fragments must not reach event details.");
    return Task.CompletedTask;
}

static async Task ProtectionMonitorRecoversAsync()
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DefenderHealthSnapshot first = HealthySnapshot(now, "1.0");
    DefenderHealthSnapshot recovered = HealthySnapshot(now.AddSeconds(2), "1.1");
    ScriptedHealthSource source = new([
        first,
        new IOException("simulated provider outage"),
        recovered
    ]);
    await using ProtectionMonitor monitor = new(
        source,
        pollInterval: TimeSpan.FromMinutes(1),
        failureRetryInterval: TimeSpan.FromMilliseconds(50),
        maximumBackoff: TimeSpan.FromMilliseconds(200));

    ProtectionMonitorUpdate current = await monitor.RefreshOnceAsync();
    Equal(ProtectionMonitorState.Current, current.State);
    Equal(0, current.ConsecutiveFailures);

    ProtectionMonitorUpdate degraded = await monitor.RefreshOnceAsync();
    Equal(ProtectionMonitorState.Degraded, degraded.State);
    Equal(1, degraded.ConsecutiveFailures);
    Equal(TimeSpan.FromMilliseconds(50), degraded.NextRefreshIn);
    Equal(first, degraded.LastKnownGood);

    ProtectionMonitorUpdate restored = await monitor.RefreshOnceAsync();
    Equal(ProtectionMonitorState.Recovered, restored.State);
    Equal(0, restored.ConsecutiveFailures);
    Equal(recovered, restored.LastKnownGood);
    True(restored.Detail.Contains("recovered", StringComparison.OrdinalIgnoreCase), "Recovery should be explicit.");
}

static async Task ProtectionMonitorIsolatesFaultsAsync()
{
    const string privateDetail = @"C:\Users\example\private\health.json";
    ScriptedHealthSource source = new([
        new FormatException(privateDetail),
        HealthySnapshot(DateTimeOffset.UtcNow, "recovered")
    ]);
    await using ProtectionMonitor monitor = new(
        source,
        pollInterval: TimeSpan.FromMinutes(1),
        failureRetryInterval: TimeSpan.FromMilliseconds(50),
        maximumBackoff: TimeSpan.FromMilliseconds(200));
    int notifications = 0;
    monitor.Updated += _ =>
    {
        Interlocked.Increment(ref notifications);
        throw new FormatException("simulated subscriber failure");
    };

    ProtectionMonitorUpdate degraded = await monitor.RefreshOnceAsync();
    Equal(ProtectionMonitorState.Degraded, degraded.State);
    True(
        !degraded.Detail.Contains(privateDetail, StringComparison.OrdinalIgnoreCase),
        "Provider exception details must not enter monitoring output.");

    ProtectionMonitorUpdate recovered = await monitor.RefreshOnceAsync();
    Equal(ProtectionMonitorState.Recovered, recovered.State);
    Equal(2, Volatile.Read(ref notifications));
}

static async Task ProtectionMonitorShutdownRaceAsync()
{
    ScriptedHealthSource source = new([HealthySnapshot(DateTimeOffset.UtcNow, "1.0")]);
    ProtectionMonitor monitor = new(
        source,
        pollInterval: TimeSpan.FromMilliseconds(25),
        failureRetryInterval: TimeSpan.FromMilliseconds(10),
        maximumBackoff: TimeSpan.FromMilliseconds(40));
    await monitor.RefreshOnceAsync();
    monitor.Start();
    monitor.RequestRefresh();
    await monitor.DisposeAsync();
}

static async Task ProtectionMonitorRefreshAfterPollsAsync()
{
    int observations = 0;
    DelegatingHealthSource source = new(cancellationToken =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        int observation = Interlocked.Increment(ref observations);
        return Task.FromResult(HealthySnapshot(
            DateTimeOffset.UtcNow,
            observation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    });
    await using ProtectionMonitor monitor = new(
        source,
        pollInterval: TimeSpan.FromMilliseconds(500),
        failureRetryInterval: TimeSpan.FromMilliseconds(50),
        maximumBackoff: TimeSpan.FromMilliseconds(200));

    monitor.Start();
    await WaitUntilAsync(
        () => Volatile.Read(ref observations) >= 3,
        TimeSpan.FromSeconds(2),
        "Monitor did not complete the expected polling cycles.");
    await Task.Delay(25);

    int beforeRefresh = Volatile.Read(ref observations);
    True(monitor.RequestRefresh(), "Expected the bounded refresh signal to be accepted.");
    await WaitUntilAsync(
        () => Volatile.Read(ref observations) > beforeRefresh,
        TimeSpan.FromMilliseconds(200),
        "A refresh signal was consumed by an abandoned polling wait.");
}

static Task ProviderNeutralHealthPrecedenceAsync()
{
    DefenderHealthSnapshot normal = HealthySnapshot(DateTimeOffset.UtcNow, "1.0");
    DefenderHealthSnapshot goodWithPassiveDefender = normal with
    {
        AMRunningMode = "Passive"
    };
    True(
        goodWithPassiveDefender.IsProtected,
        "WSC Good must remain authoritative when another provider makes Defender passive.");

    foreach (WindowsSecurityHealth explicitWarning in new[]
             {
                 WindowsSecurityHealth.Poor,
                 WindowsSecurityHealth.Snoozed,
                 WindowsSecurityHealth.NotMonitored
             })
    {
        DefenderHealthSnapshot warning = normal with
        {
            WindowsSecurityCenterHealth = explicitWarning
        };
        True(
            !warning.IsProtected,
            $"Defender detail flags must not override an explicit WSC {explicitWarning} state.");
    }

    DefenderHealthSnapshot poor = normal with
    {
        WindowsSecurityCenterHealth = WindowsSecurityHealth.Poor
    };
    True(
        poor.Summary.Contains("attention", StringComparison.OrdinalIgnoreCase),
        "An explicit WSC Poor state must remain visible to the user.");

    DefenderHealthSnapshot unknownPrimary = normal with
    {
        WindowsSecurityCenterHealth = WindowsSecurityHealth.Unknown
    };
    True(
        unknownPrimary.IsProtected,
        "Primary active Defender may provide a bounded fallback when WSC is unavailable.");
    True(
        unknownPrimary.Summary.Contains("Security Center health is unavailable", StringComparison.OrdinalIgnoreCase),
        "The fallback summary must disclose that aggregate WSC health is unavailable.");

    DefenderHealthSnapshot unknownPassive = unknownPrimary with
    {
        AMRunningMode = "Passive"
    };
    True(
        !unknownPassive.IsProtected,
        "Passive Defender must not substitute for unknown registered-provider health.");
    return Task.CompletedTask;
}

static Task NativeImportsUseSystem32Async()
{
    DefaultDllImportSearchPathsAttribute? policy = typeof(AmsiContentScanner)
        .Assembly
        .GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
    True(policy is not null, "The security assembly must declare a native DLL search policy.");
    Equal(DllImportSearchPath.System32, policy!.Paths);
    return Task.CompletedTask;
}

static Task PowerShellModulesArePinnedAsync()
{
    Type client = typeof(PowerShellDefenderClient);
    string defenderImport = GetPrivateConstant(client, "DefenderModuleImportScript");
    string diagnosticsImport = GetPrivateConstant(client, "DiagnosticsModuleImportScript");
    string utilityImport = GetPrivateConstant(client, "UtilityModuleImportScript");
    string statusScript = GetPrivateConstant(client, "StatusScript");
    string eventScript = GetPrivateConstant(client, "EventScript");
    string quickScan = GetPrivateConstant(client, "QuickScanCommand");
    string customScan = GetPrivateConstant(client, "CustomScanCommand");
    string update = GetPrivateConstant(client, "UpdateIntelligenceCommand");
    string successOutput = GetPrivateConstant(client, "SuccessOutputCommand");

    True(
        defenderImport.Contains("[System.IO.Path]::Combine", StringComparison.Ordinal) &&
        defenderImport.Contains("$PSHOME", StringComparison.Ordinal) &&
        defenderImport.Contains("Defender.psd1", StringComparison.Ordinal),
        "Defender commands must import the system manifest through $PSHOME.");
    True(
        diagnosticsImport.Contains("[System.IO.Path]::Combine", StringComparison.Ordinal) &&
        diagnosticsImport.Contains("$PSHOME", StringComparison.Ordinal) &&
        diagnosticsImport.Contains("Microsoft.PowerShell.Diagnostics.psd1", StringComparison.Ordinal),
        "Event commands must import the system diagnostics manifest through $PSHOME.");
    True(
        utilityImport.Contains("[System.IO.Path]::Combine", StringComparison.Ordinal) &&
        utilityImport.Contains("$PSHOME", StringComparison.Ordinal) &&
        utilityImport.Contains("Microsoft.PowerShell.Utility.psd1", StringComparison.Ordinal),
        "JSON commands must import the system Utility manifest through $PSHOME.");
    True(
        statusScript.Contains("Defender\\Get-MpComputerStatus", StringComparison.Ordinal) &&
        statusScript.Contains("Defender\\Get-MpPreference", StringComparison.Ordinal),
        "Defender status commands must be module-qualified.");
    True(
        eventScript.Contains("Microsoft.PowerShell.Diagnostics\\Get-WinEvent", StringComparison.Ordinal),
        "The event query must be module-qualified.");
    True(
        eventScript.Contains("-ErrorAction Stop", StringComparison.Ordinal) &&
        eventScript.Contains("FullyQualifiedErrorId", StringComparison.Ordinal) &&
        eventScript.Contains("NoMatchingEventsFound", StringComparison.Ordinal) &&
        !eventScript.Contains("SilentlyContinue", StringComparison.Ordinal),
        "The event query must distinguish an empty result from provider or access failures.");
    True(
        statusScript.Contains("Microsoft.PowerShell.Utility\\ConvertTo-Json", StringComparison.Ordinal) &&
        eventScript.Contains("Microsoft.PowerShell.Utility\\ConvertTo-Json", StringComparison.Ordinal) &&
        successOutput.Contains("Microsoft.PowerShell.Utility\\ConvertTo-Json", StringComparison.Ordinal),
        "Every JSON serializer command must be module-qualified.");
    True(
        quickScan.StartsWith("Defender\\Start-MpScan", StringComparison.Ordinal) &&
        customScan.StartsWith("Defender\\Start-MpScan", StringComparison.Ordinal) &&
        update.StartsWith("Defender\\Update-MpSignature", StringComparison.Ordinal),
        "Defender mutation commands must be module-qualified and fixed.");
    return Task.CompletedTask;
}

static Task WindowsSecurityChangeRegistrationAsync()
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    using WindowsSecurityChangeMonitor monitor = new();
    stopwatch.Stop();
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "WSC change registration exceeded two seconds.");
    Console.WriteLine(
        $"      registered={monitor.IsRegistered}; duration={stopwatch.Elapsed.TotalMilliseconds:F1} ms" +
        (monitor.Error is null ? string.Empty : $"; detail={monitor.Error}"));

    bool laterSubscriberRan = false;
    monitor.Changed += (_, _) => throw new FormatException("simulated subscriber failure");
    monitor.Changed += (_, _) => laterSubscriberRan = true;
    MethodInfo? raiseChanged = typeof(WindowsSecurityChangeMonitor).GetMethod(
        "RaiseChanged",
        BindingFlags.Instance | BindingFlags.NonPublic);
    True(raiseChanged is not null, "The WSC change dispatcher was not found.");
    raiseChanged!.Invoke(monitor, parameters: null);
    True(laterSubscriberRan, "One failed WSC subscriber must not suppress later subscribers.");
    return Task.CompletedTask;
}

static async Task DefenderHealthQueryAsync()
{
    PowerShellDefenderClient defender = new();
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(25));
    Stopwatch stopwatch = Stopwatch.StartNew();
    DefenderHealthSnapshot health = await defender.GetHealthAsync(timeout.Token);
    stopwatch.Stop();
    True(health.CheckedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1), "Health snapshot timestamp was stale.");
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "Protection health query exceeded 20 seconds.");
    Console.WriteLine(
        $"      WSC={health.WindowsSecurityCenterHealth}; mode={health.AMRunningMode ?? "unavailable"}; " +
        $"duration={stopwatch.Elapsed.TotalMilliseconds:F1} ms; {health.Summary}");
}

static async Task DefenderEventQueryAsync()
{
    PowerShellDefenderClient defender = new();
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));
    DefenderEventQueryResult result = await defender.GetRecentEventsAsync(
        TimeSpan.FromDays(7),
        maximumEvents: 16,
        timeout.Token);
    True(result.Duration < TimeSpan.FromSeconds(10), "Defender event query exceeded ten seconds.");
    True(result.Succeeded, result.Error ?? "Defender event query failed.");
    True(result.Events.Count <= 16, "Defender event query exceeded its result bound.");
    True(result.Events.All(item => !string.IsNullOrWhiteSpace(item.Title)), "Every event needs a safe title.");
    Console.WriteLine($"      events={result.Events.Count}; duration={result.Duration.TotalMilliseconds:F1} ms");
}

static async Task DefenderOutputIsBoundedAsync()
{
    string executable = GetTestExecutablePath();
    const string variable = "SOLTEX_TEST_CHILD_MODE";
    string? previous = Environment.GetEnvironmentVariable(variable);
    try
    {
        Environment.SetEnvironmentVariable(variable, "oversized-output");
        PowerShellDefenderClient defender = new(executable);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        DefenderHealthSnapshot health = await defender.GetHealthAsync(timeout.Token);
        True(!health.StatusQuerySucceeded, "Oversized child output must not be parsed as Defender status.");
        True(
            health.Error?.Contains("safety limit", StringComparison.OrdinalIgnoreCase) == true,
            $"Expected an explicit output-limit failure, got: {health.Error ?? "no error"}");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previous);
    }
}

static async Task PowerShellChildBoundaryIsHardenedAsync()
{
    string executable = GetTestExecutablePath();
    const string variable = "SOLTEX_TEST_CHILD_MODE";
    string? previous = Environment.GetEnvironmentVariable(variable);
    try
    {
        PowerShellDefenderClient defender = new(executable);
        Environment.SetEnvironmentVariable(variable, "report-working-directory");
        DefenderHealthSnapshot directoryReport = await defender.GetHealthAsync();
        string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        True(
            string.Equals(expectedDirectory, directoryReport.AMRunningMode, StringComparison.OrdinalIgnoreCase),
            $"Expected child working directory '{expectedDirectory}', got '{directoryReport.AMRunningMode}'.");

        Environment.SetEnvironmentVariable(variable, "sensitive-error");
        DefenderHealthSnapshot failed = await defender.GetHealthAsync();
        True(!failed.StatusQuerySucceeded, "A nonzero child exit must fail the Defender query.");
        True(
            failed.Error?.Contains("exit code 23", StringComparison.OrdinalIgnoreCase) == true,
            $"Expected a bounded exit-code diagnostic, got: {failed.Error ?? "no error"}");
        True(
            failed.Error?.Contains("private", StringComparison.OrdinalIgnoreCase) == false,
            "Raw PowerShell stderr must not enter health or audit-facing output.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previous);
    }
}

static async Task DefenderEventEnvelopeIsBoundedAsync()
{
    string executable = GetTestExecutablePath();
    const string variable = "SOLTEX_TEST_CHILD_MODE";
    string? previous = Environment.GetEnvironmentVariable(variable);
    try
    {
        Environment.SetEnvironmentVariable(variable, "oversized-event-envelope");
        PowerShellDefenderClient defender = new(executable);
        DefenderEventQueryResult result = await defender.GetRecentEventsAsync(
            TimeSpan.FromMinutes(1),
            maximumEvents: 1);
        True(!result.Succeeded, "An oversized event envelope must be rejected.");
        Equal(0, result.Events.Count);
        True(
            result.Error?.Contains("requested 1", StringComparison.OrdinalIgnoreCase) == true,
            $"Expected an explicit item-count failure, got: {result.Error ?? "no error"}");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previous);
    }
}

static Task SecurityObservationPerformanceAsync()
{
    Stopwatch wscStopwatch = Stopwatch.StartNew();
    for (int index = 0; index < 250; index++)
    {
        _ = WindowsSecurityCenter.GetAntivirusHealth();
    }

    wscStopwatch.Stop();

    byte[] fixture = Encoding.UTF8.GetBytes(new string('A', 4_096));
    using IContentScanner scanner = AmsiContentScanner.CreateOrUnavailable();
    Stopwatch amsiStopwatch = Stopwatch.StartNew();
    for (int index = 0; index < 32; index++)
    {
        ContentScanResult result = scanner.Scan(fixture, "performance-fixture.txt");
        True(!result.ShouldBlock, $"Unexpected block during benign performance probe: {result.Detail}");
    }

    amsiStopwatch.Stop();
    double wscMeanMs = wscStopwatch.Elapsed.TotalMilliseconds / 250;
    double amsiMeanMs = amsiStopwatch.Elapsed.TotalMilliseconds / 32;
    True(wscMeanMs < 20, "Mean WSC observation time exceeded 20 ms.");
    True(amsiMeanMs < 250, "Mean 4 KiB AMSI observation time exceeded 250 ms.");
    Console.WriteLine($"      WSC mean={wscMeanMs:F3} ms; AMSI 4 KiB mean={amsiMeanMs:F3} ms");
    return Task.CompletedTask;
}

static Task RemotePeerIdsAreConstrainedAsync()
{
    True(
        RemotePeerId.TryCreate("  desk_42-A  ", out RemotePeerId? peerId, out string validError),
        validError);
    Equal("desk_42-A", peerId!.Value);

    foreach (string invalid in new[]
             {
                 "ab",
                 "peer id",
                 "peer\" --password secret",
                 "peer;calc.exe",
                 "---"
             })
    {
        True(
            !RemotePeerId.TryCreate(invalid, out _, out string error),
            $"Expected peer ID rejection for '{invalid}', but it was accepted: {error}");
    }

    return Task.CompletedTask;
}

static async Task RemoteLaunchPlansAreFixedAsync()
{
    await WithTempDirectoryAsync(async root =>
    {
        string rustDeskPath = Path.Combine(root, "RustDesk.exe");
        await File.WriteAllBytesAsync(rustDeskPath, [0x4D, 0x5A]);
        True(
            RemoteAssistExecutable.TryCreate(
                rustDeskPath,
                out RemoteAssistExecutable? executable,
                out string executableError),
            executableError);
        True(
            RemotePeerId.TryCreate("peer-123", out RemotePeerId? peerId, out string peerError),
            peerError);

        RemoteAssistLaunchPlan sharePlan = RustDeskExternalClient.CreateSharePlan(executable!);
        Equal(RemoteAssistMode.ShareThisDevice, sharePlan.Mode);
        Equal(0, sharePlan.Arguments.Count);

        RemoteAssistLaunchPlan controlPlan = RustDeskExternalClient.CreateControlPlan(executable!, peerId!);
        Equal(RemoteAssistMode.ControlPeer, controlPlan.Mode);
        Equal(2, controlPlan.Arguments.Count);
        Equal("--connect", controlPlan.Arguments[0]);
        Equal("peer-123", controlPlan.Arguments[1]);

        ProcessStartInfo startInfo = RustDeskExternalClient.CreateStartInfo(controlPlan);
        True(!startInfo.UseShellExecute, "Remote Assist must never invoke a command shell.");
        Equal(executable!.FullPath, startInfo.FileName);
        Equal(2, startInfo.ArgumentList.Count);
        Equal("--connect", startInfo.ArgumentList[0]);
        Equal("peer-123", startInfo.ArgumentList[1]);
        True(
            startInfo.ArgumentList.All(argument =>
                !argument.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                !argument.Contains("elevat", StringComparison.OrdinalIgnoreCase) &&
                !argument.Contains("service", StringComparison.OrdinalIgnoreCase)),
            "Launch arguments must not contain unattended-access, elevation, or service controls.");

        True(executable.VerifyUnchanged(out string unchangedError), unchangedError);
        await File.AppendAllTextAsync(rustDeskPath, "changed");
        True(
            !executable.VerifyUnchanged(out string changedError),
            "The approved executable fingerprint must reject a later file replacement.");
        True(
            changedError.Contains("changed", StringComparison.OrdinalIgnoreCase),
            "Fingerprint failure should require explicit reapproval.");
    });
}

static async Task RemoteExecutableDiscoveryIsBoundedAsync()
{
    IReadOnlyList<string> candidates = RemoteAssistExecutableLocator.GetBoundedCandidates();
    True(candidates.Count <= 2, "Executable discovery must remain limited to Program Files roots.");
    foreach (string candidate in candidates)
    {
        True(Path.IsPathFullyQualified(candidate), "Discovery candidates must be absolute paths.");
        Equal("RustDesk.exe", Path.GetFileName(candidate));
    }

    await WithTempDirectoryAsync(async root =>
    {
        string wrongName = Path.Combine(root, "remote.exe");
        await File.WriteAllBytesAsync(wrongName, [0x4D, 0x5A]);
        True(
            !RemoteAssistExecutable.TryCreate(wrongName, out _, out string error),
            "An executable with a different filename must not cross the RustDesk boundary.");
        True(
            error.Contains("RustDesk.exe", StringComparison.OrdinalIgnoreCase),
            "Filename rejection should explain the fixed external-client boundary.");
    });
}

static Task AmsiDetectsEicarAsync()
{
    string marker = string.Concat(
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-",
        "STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
    using IContentScanner scanner = AmsiContentScanner.CreateOrUnavailable();
    ContentScanResult result = scanner.Scan(Encoding.ASCII.GetBytes(marker), "eicar.com.txt");
    True(result.ShouldBlock, $"Installed AMSI provider did not block EICAR (result {result.NativeResult}).");
    return Task.CompletedTask;
}

static DefenderHealthSnapshot HealthySnapshot(DateTimeOffset checkedAtUtc, string signatureVersion) => new(
    checkedAtUtc,
    WindowsSecurityHealth.Good,
    StatusQuerySucceeded: true,
    AMRunningMode: "Normal",
    AMServiceEnabled: true,
    AntivirusEnabled: true,
    RealTimeProtectionEnabled: true,
    BehaviorMonitorEnabled: true,
    IoavProtectionEnabled: true,
    NetworkInspectionEnabled: true,
    TamperProtected: true,
    CloudProtectionEnabled: true,
    SignaturesOutOfDate: false,
    AntivirusSignatureVersion: signatureVersion,
    AntivirusSignatureUpdatedAt: checkedAtUtc,
    QuickScanAgeDays: 0,
    FullScanAgeDays: 1,
    Error: null);

static async Task WithTempDirectoryAsync(Func<string, Task> action)
{
    string root = Path.Combine(Path.GetTempPath(), "Soltex.Tests", Guid.NewGuid().ToString("N"));
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

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (!condition())
    {
        if (stopwatch.Elapsed >= timeout)
        {
            throw new InvalidOperationException(failureMessage);
        }

        await Task.Delay(10);
    }
}

static string GetPrivateConstant(Type type, string name)
{
    FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException($"Private constant '{name}' was not found.");
    return field.GetRawConstantValue() as string ??
        throw new InvalidOperationException($"Private constant '{name}' was not a string.");
}

static string GetTestExecutablePath()
{
    string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
    string? appHostPath = string.IsNullOrWhiteSpace(entryAssemblyPath)
        ? null
        : Path.ChangeExtension(entryAssemblyPath, ".exe");
    return appHostPath is not null && File.Exists(appHostPath)
        ? appHostPath
        : Environment.ProcessPath ??
          throw new InvalidOperationException("The test process path is unavailable.");
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

sealed class ScriptedHealthSource(IEnumerable<object> script) : IProtectionHealthSource
{
    private readonly Queue<object> _script = new(script);

    public Task<DefenderHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_script.Count == 0)
        {
            throw new InvalidOperationException("No scripted health observation remains.");
        }

        object next = _script.Dequeue();
        return next switch
        {
            DefenderHealthSnapshot snapshot => Task.FromResult(snapshot),
            Exception exception => Task.FromException<DefenderHealthSnapshot>(exception),
            _ => throw new InvalidOperationException("Unsupported scripted health observation.")
        };
    }
}

sealed class DelegatingHealthSource(
    Func<CancellationToken, Task<DefenderHealthSnapshot>> getHealth) : IProtectionHealthSource
{
    public Task<DefenderHealthSnapshot> GetHealthAsync(
        CancellationToken cancellationToken = default) => getHealth(cancellationToken);
}
