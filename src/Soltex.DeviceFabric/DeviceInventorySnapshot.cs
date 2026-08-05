using System.Collections.ObjectModel;

namespace Soltex.DeviceFabric;

public sealed class DeviceInventorySnapshot
{
    public const int MaximumDeviceCount = 128;

    private DeviceInventorySnapshot(
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<DeviceManifest> devices)
    {
        CapturedAtUtc = capturedAtUtc;
        Devices = devices;
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public IReadOnlyList<DeviceManifest> Devices { get; }

    public static bool TryCreate(
        DateTimeOffset capturedAtUtc,
        IEnumerable<DeviceManifest>? devices,
        out DeviceInventorySnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        if (capturedAtUtc == default)
        {
            error = "The inventory capture time is required.";
            return false;
        }

        if (devices is null)
        {
            error = "A device collection is required.";
            return false;
        }

        List<DeviceManifest> accepted = [];
        HashSet<string> seenDeviceIds = new(StringComparer.Ordinal);
        foreach (DeviceManifest? device in devices)
        {
            if (accepted.Count >= MaximumDeviceCount)
            {
                error = $"Inventory snapshots may contain at most {MaximumDeviceCount} devices.";
                return false;
            }

            if (device is null)
            {
                error = "Inventory snapshots cannot contain a null device.";
                return false;
            }

            if (!seenDeviceIds.Add(device.DeviceId))
            {
                error = "Inventory snapshots cannot contain duplicate device IDs.";
                return false;
            }

            accepted.Add(device);
        }

        snapshot = new DeviceInventorySnapshot(
            capturedAtUtc.ToUniversalTime(),
            Array.AsReadOnly(accepted.ToArray()));
        error = string.Empty;
        return true;
    }
}
