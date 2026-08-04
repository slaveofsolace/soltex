using System.Collections.ObjectModel;

namespace Soltex.DeviceFabric;

public enum DevicePlatform
{
    Windows,
    MacOS,
    Linux,
    NetworkStorage
}

public sealed class DeviceManifest
{
    public const int SchemaVersion = 1;
    public const int MaximumCapabilityCount = 32;
    public const int MaximumDeviceIdLength = 64;
    public const int MaximumDisplayNameLength = 48;
    public const int MaximumAgentVersionLength = 32;

    private DeviceManifest(
        string deviceId,
        string displayName,
        DevicePlatform platform,
        string agentVersion,
        IReadOnlyList<string> capabilityIds)
    {
        Version = SchemaVersion;
        DeviceId = deviceId;
        DisplayName = displayName;
        Platform = platform;
        AgentVersion = agentVersion;
        CapabilityIds = capabilityIds;
    }

    public int Version { get; }

    public string DeviceId { get; }

    public string DisplayName { get; }

    public DevicePlatform Platform { get; }

    public string AgentVersion { get; }

    public IReadOnlyList<string> CapabilityIds { get; }

    public static bool TryCreate(
        string? deviceId,
        string? displayName,
        DevicePlatform platform,
        string? agentVersion,
        IEnumerable<string>? capabilityIds,
        out DeviceManifest? manifest,
        out string error)
    {
        manifest = null;
        if (!IsCanonicalDeviceId(deviceId))
        {
            error = "Device IDs must be 3 to 64 lowercase ASCII letters, numbers, or single hyphens, with an alphanumeric first and last character.";
            return false;
        }

        if (!IsSafeDisplayName(displayName))
        {
            error = "Device names must be 1 to 48 printable ASCII letters, numbers, spaces, periods, parentheses, underscores, or hyphens.";
            return false;
        }

        if (!Enum.IsDefined(platform))
        {
            error = "The device platform is not recognized.";
            return false;
        }

        if (!IsCanonicalVersion(agentVersion))
        {
            error = "Agent versions must be 1 to 32 ASCII digits and periods in a valid version form.";
            return false;
        }

        if (capabilityIds is null)
        {
            error = "A capability list is required.";
            return false;
        }

        List<string> accepted = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? capabilityId in capabilityIds)
        {
            if (accepted.Count >= MaximumCapabilityCount)
            {
                error = $"Device manifests may contain at most {MaximumCapabilityCount} capabilities.";
                return false;
            }

            if (!CapabilityCatalog.TryGet(capabilityId, out _))
            {
                error = "The manifest contains a capability outside the exact Soltex catalog.";
                return false;
            }

            if (!seen.Add(capabilityId!))
            {
                error = "The manifest contains a duplicate capability.";
                return false;
            }

            if (platform != DevicePlatform.Windows &&
                capabilityId is global::Soltex.DeviceFabric.CapabilityIds.DefenderQuickScanRequest or
                    global::Soltex.DeviceFabric.CapabilityIds.DefenderIntelligenceUpdateRequest)
            {
                error = "Defender request capabilities may only be declared by Windows devices.";
                return false;
            }

            accepted.Add(capabilityId!);
        }

        if (accepted.Count == 0)
        {
            error = "A device manifest must declare at least one typed capability.";
            return false;
        }

        manifest = new DeviceManifest(
            deviceId!,
            displayName!,
            platform,
            agentVersion!,
            Array.AsReadOnly(accepted.ToArray()));
        error = string.Empty;
        return true;
    }

    private static bool IsCanonicalDeviceId(string? value)
    {
        if (value is null || value.Length is < 3 or > MaximumDeviceIdLength ||
            !char.IsAsciiLetterOrDigit(value[0]) ||
            !char.IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        bool previousWasHyphen = false;
        foreach (char character in value)
        {
            bool isLowerAscii = character is >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            if (!isLowerAscii && !isDigit && character != '-')
            {
                return false;
            }

            if (character == '-' && previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = character == '-';
        }

        return true;
    }

    private static bool IsSafeDisplayName(string? value)
    {
        if (value is null || value.Length is < 1 or > MaximumDisplayNameLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        bool hasLetterOrDigit = false;
        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                hasLetterOrDigit = true;
                continue;
            }

            if (character is not ' ' and not '.' and not '(' and not ')' and not '_' and not '-')
            {
                return false;
            }
        }

        return hasLetterOrDigit;
    }

    private static bool IsCanonicalVersion(string? value)
    {
        if (value is null || value.Length is < 1 or > MaximumAgentVersionLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character) && character != '.')
            {
                return false;
            }
        }

        return System.Version.TryParse(value, out System.Version? parsed) &&
            parsed.Major >= 0;
    }
}
