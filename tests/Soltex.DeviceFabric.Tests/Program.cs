using System.Diagnostics;
using Soltex.DeviceFabric;

List<(string Name, Action Test)> tests =
[
    ("The capability catalog is exact and bounded", CapabilityCatalogIsExact),
    ("Read-only observations require device-local policy", ReadOnlyPolicyIsRequired),
    ("State-changing requests require visible local consent", StateChangeRequiresConsent),
    ("Remote handoff requires a visible external client", RemoteHandoffRequiresVisibleClient),
    ("A target manifest is required", TargetManifestIsRequired),
    ("Known but unadvertised capabilities are denied", UnadvertisedCapabilityIsDenied),
    ("Generic shell capability is denied", GenericShellIsDenied),
    ("Arbitrary download-execute capability is denied", DownloadExecuteIsDenied),
    ("Hidden remote-control capability is denied", HiddenControlIsDenied),
    ("A valid Windows manifest is accepted", ValidWindowsManifestIsAccepted),
    ("Manifest capability input is copied", ManifestCapabilityInputIsCopied),
    ("Duplicate capabilities are rejected", DuplicateCapabilitiesAreRejected),
    ("Unknown capabilities are rejected", UnknownCapabilitiesAreRejected),
    ("Defender requests are Windows-only", DefenderRequestsAreWindowsOnly),
    ("Device ID injection is rejected", DeviceIdInjectionIsRejected),
    ("Display-name injection is rejected", DisplayNameInjectionIsRejected),
    ("Invalid agent versions are rejected", InvalidAgentVersionIsRejected),
    ("Duplicate inventory device IDs are rejected", DuplicateDeviceIdsAreRejected),
    ("Inventory input is copied and capture time normalized", InventoryInputIsCopied),
    ("Oversized inventories fail closed", OversizedInventoryIsRejected)
];

int failed = 0;
Stopwatch suiteTimer = Stopwatch.StartNew();
foreach ((string name, Action test) in tests)
{
    Stopwatch testTimer = Stopwatch.StartNew();
    try
    {
        test();
        testTimer.Stop();
        Console.WriteLine($"PASS  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
    }
    catch (Exception exception)
    {
        testTimer.Stop();
        failed++;
        Console.WriteLine($"FAIL  {name} ({testTimer.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine("      " + exception.Message);
    }
}

suiteTimer.Stop();
Console.WriteLine();
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
Console.WriteLine($"MEASURE device_fabric_suite tests={tests.Count} failed={failed} total_ms={suiteTimer.Elapsed.TotalMilliseconds:F1}");
return failed == 0 ? 0 : 1;

static void CapabilityCatalogIsExact()
{
    Equal(6, CapabilityCatalog.All.Count);
    Equal(
        string.Join('|',
        [
            CapabilityIds.SystemHealthObserve,
            CapabilityIds.SecurityProtectionObserve,
            CapabilityIds.UpdateJournalInspect,
            CapabilityIds.DefenderQuickScanRequest,
            CapabilityIds.DefenderIntelligenceUpdateRequest,
            CapabilityIds.RustDeskHandoffPrepare
        ]),
        string.Join('|', CapabilityCatalog.All.Select(capability => capability.Id)));
    Equal(
        3,
        CapabilityCatalog.All.Count(capability =>
            capability.Effect == CapabilityEffect.ReadOnlyObservation));
    Equal(
        3,
        CapabilityCatalog.All.Count(capability =>
            capability.ApprovalMode == CapabilityApprovalMode.PerJobVisibleLocalConsent));
}

static void ReadOnlyPolicyIsRequired()
{
    DeviceManifest target = CreateManifest(
        "studio-windows-01",
        [CapabilityIds.SystemHealthObserve]);
    CapabilityPolicyDecision denied = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.SystemHealthObserve,
        readOnlyPolicyAllows: false,
        hasVisibleLocalConsent: false,
        externalClientVisible: false);
    Equal(CapabilityPolicyStatus.DeniedByDevicePolicy, denied.Status);
    True(!denied.Allowed, "A disabled device-local observation policy was bypassed.");

    CapabilityPolicyDecision allowed = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.SystemHealthObserve,
        readOnlyPolicyAllows: true,
        hasVisibleLocalConsent: false,
        externalClientVisible: false);
    Equal(CapabilityPolicyStatus.AllowedByDevicePolicy, allowed.Status);
    True(allowed.Allowed, "An explicitly permitted read-only observation was denied.");
}

static void StateChangeRequiresConsent()
{
    DeviceManifest target = CreateManifest(
        "studio-windows-01",
        [CapabilityIds.DefenderQuickScanRequest]);
    CapabilityPolicyDecision denied = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.DefenderQuickScanRequest,
        readOnlyPolicyAllows: true,
        hasVisibleLocalConsent: false,
        externalClientVisible: true);
    Equal(CapabilityPolicyStatus.DeniedVisibleLocalConsentRequired, denied.Status);

    CapabilityPolicyDecision allowed = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.DefenderQuickScanRequest,
        readOnlyPolicyAllows: false,
        hasVisibleLocalConsent: true,
        externalClientVisible: false);
    Equal(CapabilityPolicyStatus.AllowedByVisibleLocalConsent, allowed.Status);
}

static void RemoteHandoffRequiresVisibleClient()
{
    DeviceManifest target = CreateManifest(
        "studio-windows-01",
        [CapabilityIds.RustDeskHandoffPrepare]);
    CapabilityPolicyDecision hidden = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.RustDeskHandoffPrepare,
        readOnlyPolicyAllows: false,
        hasVisibleLocalConsent: true,
        externalClientVisible: false);
    Equal(CapabilityPolicyStatus.DeniedExternalClientNotVisible, hidden.Status);

    CapabilityPolicyDecision visible = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.RustDeskHandoffPrepare,
        readOnlyPolicyAllows: false,
        hasVisibleLocalConsent: true,
        externalClientVisible: true);
    Equal(CapabilityPolicyStatus.AllowedByVisibleLocalConsent, visible.Status);
}

static void TargetManifestIsRequired()
{
    CapabilityPolicyDecision decision = CapabilityPolicy.Evaluate(
        null,
        CapabilityIds.SystemHealthObserve,
        readOnlyPolicyAllows: true,
        hasVisibleLocalConsent: true,
        externalClientVisible: true);
    Equal(CapabilityPolicyStatus.DeniedTargetManifestRequired, decision.Status);
}

static void UnadvertisedCapabilityIsDenied()
{
    DeviceManifest target = CreateManifest(
        "studio-windows-01",
        [CapabilityIds.SystemHealthObserve]);
    CapabilityPolicyDecision decision = CapabilityPolicy.Evaluate(
        target,
        CapabilityIds.DefenderQuickScanRequest,
        readOnlyPolicyAllows: true,
        hasVisibleLocalConsent: true,
        externalClientVisible: true);
    Equal(CapabilityPolicyStatus.DeniedCapabilityNotAdvertised, decision.Status);
}

static void GenericShellIsDenied() =>
    UnknownCapabilityIsDenied("command.shell.execute");

static void DownloadExecuteIsDenied() =>
    UnknownCapabilityIsDenied("software.download_and_execute");

static void HiddenControlIsDenied() =>
    UnknownCapabilityIsDenied("remote.desktop.unattended_control");

static void UnknownCapabilityIsDenied(string capabilityId)
{
    CapabilityPolicyDecision decision = CapabilityPolicy.Evaluate(
        CreateManifest("studio-windows-01"),
        capabilityId,
        readOnlyPolicyAllows: true,
        hasVisibleLocalConsent: true,
        externalClientVisible: true);
    Equal(CapabilityPolicyStatus.DeniedUnknownCapability, decision.Status);
    True(!decision.Allowed, $"Unknown capability was permitted: {capabilityId}");
}

static void ValidWindowsManifestIsAccepted()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01",
        "Studio Windows (Primary)",
        DevicePlatform.Windows,
        "1.0.0",
        [
            CapabilityIds.SystemHealthObserve,
            CapabilityIds.SecurityProtectionObserve,
            CapabilityIds.DefenderQuickScanRequest,
            CapabilityIds.RustDeskHandoffPrepare
        ],
        out DeviceManifest? manifest,
        out string error);
    True(created, error);
    Equal(4, manifest!.CapabilityIds.Count);
    Equal(DeviceManifest.SchemaVersion, manifest.Version);
}

static void ManifestCapabilityInputIsCopied()
{
    List<string> capabilityIds = [CapabilityIds.SystemHealthObserve];
    DeviceManifest manifest = CreateManifest("studio-windows-01", capabilityIds);
    capabilityIds.Clear();
    Equal(1, manifest.CapabilityIds.Count);
}

static void DuplicateCapabilitiesAreRejected()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01",
        "Studio Windows",
        DevicePlatform.Windows,
        "1.0.0",
        [CapabilityIds.SystemHealthObserve, CapabilityIds.SystemHealthObserve],
        out _,
        out _);
    True(!created, "A duplicate capability was accepted.");
}

static void UnknownCapabilitiesAreRejected()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01",
        "Studio Windows",
        DevicePlatform.Windows,
        "1.0.0",
        ["filesystem.arbitrary.write"],
        out _,
        out _);
    True(!created, "An unknown manifest capability was accepted.");
}

static void DefenderRequestsAreWindowsOnly()
{
    bool created = DeviceManifest.TryCreate(
        "studio-mac-01",
        "Studio Mac",
        DevicePlatform.MacOS,
        "1.0.0",
        [CapabilityIds.DefenderIntelligenceUpdateRequest],
        out _,
        out _);
    True(!created, "A macOS device declared a Windows Defender request capability.");
}

static void DeviceIdInjectionIsRejected()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01\r\nforged",
        "Studio Windows",
        DevicePlatform.Windows,
        "1.0.0",
        [CapabilityIds.SystemHealthObserve],
        out _,
        out _);
    True(!created, "A control character was accepted in a device ID.");
}

static void DisplayNameInjectionIsRejected()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01",
        "Studio Windows\nforged event",
        DevicePlatform.Windows,
        "1.0.0",
        [CapabilityIds.SystemHealthObserve],
        out _,
        out _);
    True(!created, "A control character was accepted in a display name.");
}

static void InvalidAgentVersionIsRejected()
{
    bool created = DeviceManifest.TryCreate(
        "studio-windows-01",
        "Studio Windows",
        DevicePlatform.Windows,
        "1.0.0-beta\nforged",
        [CapabilityIds.SystemHealthObserve],
        out _,
        out _);
    True(!created, "An unbounded or injectable agent version was accepted.");
}

static void DuplicateDeviceIdsAreRejected()
{
    DeviceManifest first = CreateManifest("studio-windows-01");
    DeviceManifest duplicate = CreateManifest("studio-windows-01");
    bool created = DeviceInventorySnapshot.TryCreate(
        DateTimeOffset.UtcNow,
        [first, duplicate],
        out _,
        out _);
    True(!created, "Duplicate device IDs were accepted in one snapshot.");
}

static void InventoryInputIsCopied()
{
    List<DeviceManifest> devices = [CreateManifest("studio-windows-01")];
    DateTimeOffset captured = new(2026, 8, 4, 8, 0, 0, TimeSpan.FromHours(-5));
    bool created = DeviceInventorySnapshot.TryCreate(
        captured,
        devices,
        out DeviceInventorySnapshot? snapshot,
        out string error);
    True(created, error);
    devices.Clear();
    Equal(1, snapshot!.Devices.Count);
    Equal(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
    Equal(captured.ToUniversalTime(), snapshot.CapturedAtUtc);
}

static void OversizedInventoryIsRejected()
{
    IEnumerable<DeviceManifest> oversized = Enumerable.Range(
        0,
        DeviceInventorySnapshot.MaximumDeviceCount + 1)
        .Select(index => CreateManifest($"device-{index:D3}"));
    bool created = DeviceInventorySnapshot.TryCreate(
        DateTimeOffset.UtcNow,
        oversized,
        out _,
        out _);
    True(!created, "An oversized inventory snapshot was accepted.");
}

static DeviceManifest CreateManifest(
    string deviceId,
    IEnumerable<string>? capabilities = null)
{
    bool created = DeviceManifest.TryCreate(
        deviceId,
        "Studio Windows",
        DevicePlatform.Windows,
        "1.0.0",
        capabilities ?? [CapabilityIds.SystemHealthObserve],
        out DeviceManifest? manifest,
        out string error);
    return created ? manifest! : throw new InvalidOperationException(error);
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}
