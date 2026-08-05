using System.Collections.ObjectModel;

namespace Soltex.DeviceFabric;

public static class CapabilityIds
{
    public const string SystemHealthObserve = "system.health.observe";
    public const string SecurityProtectionObserve = "security.protection.observe";
    public const string UpdateJournalInspect = "update.journal.inspect";
    public const string DefenderQuickScanRequest = "security.defender.quick_scan.request";
    public const string DefenderIntelligenceUpdateRequest =
        "security.defender.intelligence_update.request";
    public const string RustDeskHandoffPrepare = "remote.rustdesk.handoff.prepare";
}

public enum CapabilityEffect
{
    ReadOnlyObservation,
    StateChangingRequest,
    ExternalClientHandoff
}

public enum CapabilityApprovalMode
{
    DeviceLocalReadOnlyPolicy,
    PerJobVisibleLocalConsent
}

public sealed class CapabilityDescriptor
{
    internal CapabilityDescriptor(
        string id,
        string displayName,
        string description,
        CapabilityEffect effect,
        CapabilityApprovalMode approvalMode)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Effect = effect;
        ApprovalMode = approvalMode;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public CapabilityEffect Effect { get; }

    public CapabilityApprovalMode ApprovalMode { get; }
}

public static class CapabilityCatalog
{
    private static readonly ReadOnlyCollection<CapabilityDescriptor> Descriptors =
        Array.AsReadOnly(
        [
            new CapabilityDescriptor(
                CapabilityIds.SystemHealthObserve,
                "Observe system health",
                "Read a bounded device-health snapshot without changing the device.",
                CapabilityEffect.ReadOnlyObservation,
                CapabilityApprovalMode.DeviceLocalReadOnlyPolicy),
            new CapabilityDescriptor(
                CapabilityIds.SecurityProtectionObserve,
                "Observe protection health",
                "Read supported protection-provider health without weakening protection.",
                CapabilityEffect.ReadOnlyObservation,
                CapabilityApprovalMode.DeviceLocalReadOnlyPolicy),
            new CapabilityDescriptor(
                CapabilityIds.UpdateJournalInspect,
                "Inspect update journal",
                "Read the sanitized authenticated Soltex update-planning journal.",
                CapabilityEffect.ReadOnlyObservation,
                CapabilityApprovalMode.DeviceLocalReadOnlyPolicy),
            new CapabilityDescriptor(
                CapabilityIds.DefenderQuickScanRequest,
                "Request Defender quick scan",
                "Ask the local supported Defender interface to start a quick scan.",
                CapabilityEffect.StateChangingRequest,
                CapabilityApprovalMode.PerJobVisibleLocalConsent),
            new CapabilityDescriptor(
                CapabilityIds.DefenderIntelligenceUpdateRequest,
                "Request Defender intelligence update",
                "Ask the local supported Defender interface to update security intelligence.",
                CapabilityEffect.StateChangingRequest,
                CapabilityApprovalMode.PerJobVisibleLocalConsent),
            new CapabilityDescriptor(
                CapabilityIds.RustDeskHandoffPrepare,
                "Prepare RustDesk handoff",
                "Prepare a visible handoff to an explicitly selected external RustDesk client.",
                CapabilityEffect.ExternalClientHandoff,
                CapabilityApprovalMode.PerJobVisibleLocalConsent)
        ]);

    private static readonly ReadOnlyDictionary<string, CapabilityDescriptor> ById =
        new(new Dictionary<string, CapabilityDescriptor>(
            Descriptors.ToDictionary(descriptor => descriptor.Id),
            StringComparer.Ordinal));

    public static IReadOnlyList<CapabilityDescriptor> All => Descriptors;

    public static bool TryGet(
        string? capabilityId,
        out CapabilityDescriptor? descriptor)
    {
        descriptor = null;
        return capabilityId is not null && ById.TryGetValue(capabilityId, out descriptor);
    }
}

public enum CapabilityPolicyStatus
{
    AllowedByDevicePolicy,
    AllowedByVisibleLocalConsent,
    DeniedUnknownCapability,
    DeniedTargetManifestRequired,
    DeniedCapabilityNotAdvertised,
    DeniedByDevicePolicy,
    DeniedVisibleLocalConsentRequired,
    DeniedExternalClientNotVisible
}

public sealed record CapabilityPolicyDecision(
    CapabilityPolicyStatus Status,
    string Detail)
{
    public bool Allowed =>
        Status is CapabilityPolicyStatus.AllowedByDevicePolicy or
            CapabilityPolicyStatus.AllowedByVisibleLocalConsent;
}

public static class CapabilityPolicy
{
    public static CapabilityPolicyDecision Evaluate(
        DeviceManifest? targetDevice,
        string? capabilityId,
        bool readOnlyPolicyAllows,
        bool hasVisibleLocalConsent,
        bool externalClientVisible)
    {
        if (!CapabilityCatalog.TryGet(capabilityId, out CapabilityDescriptor? descriptor) ||
            descriptor is null)
        {
            return new CapabilityPolicyDecision(
                CapabilityPolicyStatus.DeniedUnknownCapability,
                "The requested capability is not in the exact Soltex capability catalog.");
        }

        if (targetDevice is null)
        {
            return new CapabilityPolicyDecision(
                CapabilityPolicyStatus.DeniedTargetManifestRequired,
                "A structurally valid target-device manifest is required for every capability decision.");
        }

        if (!targetDevice.CapabilityIds.Contains(capabilityId!, StringComparer.Ordinal))
        {
            return new CapabilityPolicyDecision(
                CapabilityPolicyStatus.DeniedCapabilityNotAdvertised,
                "The target device did not advertise this exact capability.");
        }

        if (descriptor.Effect == CapabilityEffect.ReadOnlyObservation)
        {
            return readOnlyPolicyAllows
                ? new CapabilityPolicyDecision(
                    CapabilityPolicyStatus.AllowedByDevicePolicy,
                    "The device-local policy permits this read-only observation.")
                : new CapabilityPolicyDecision(
                    CapabilityPolicyStatus.DeniedByDevicePolicy,
                    "The device-local policy does not permit this read-only observation.");
        }

        if (!hasVisibleLocalConsent)
        {
            return new CapabilityPolicyDecision(
                CapabilityPolicyStatus.DeniedVisibleLocalConsentRequired,
                "This request requires visible, per-job consent on the target device.");
        }

        if (descriptor.Effect == CapabilityEffect.ExternalClientHandoff &&
            !externalClientVisible)
        {
            return new CapabilityPolicyDecision(
                CapabilityPolicyStatus.DeniedExternalClientNotVisible,
                "The external remote-assistance client must remain visible to the local user.");
        }

        return new CapabilityPolicyDecision(
            CapabilityPolicyStatus.AllowedByVisibleLocalConsent,
            "Visible, per-job local consent authorizes this typed request.");
    }
}
