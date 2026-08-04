using System.Runtime.InteropServices;

namespace Soltex.DeviceFabric;

public enum DeviceEnrollmentState
{
    NotEnrolled
}

public enum LocalDeviceObservationState
{
    Current,
    Partial
}

public sealed record LocalDeviceObservation(
    DateTimeOffset CapturedAtUtc,
    LocalDeviceObservationState State,
    DeviceEnrollmentState EnrollmentState,
    string DisplayName,
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    string Framework,
    string Provenance,
    IReadOnlyList<string> Limitations);

public static class LocalDeviceObservationProvider
{
    public const int MaximumFieldLength = 96;

    public static LocalDeviceObservation Capture()
    {
        List<string> limitations = [];
        string displayName = ReadBounded(
            static () => Environment.MachineName,
            "Local machine",
            "The Windows machine name was unavailable.",
            limitations);
        string operatingSystem = ReadBounded(
            static () => RuntimeInformation.OSDescription,
            "Unavailable",
            "The operating-system description was unavailable.",
            limitations);
        string osArchitecture = ReadBounded(
            static () => RuntimeInformation.OSArchitecture.ToString(),
            "Unavailable",
            "The operating-system architecture was unavailable.",
            limitations);
        string processArchitecture = ReadBounded(
            static () => RuntimeInformation.ProcessArchitecture.ToString(),
            "Unavailable",
            "The process architecture was unavailable.",
            limitations);
        string framework = ReadBounded(
            static () => RuntimeInformation.FrameworkDescription,
            "Unavailable",
            "The .NET runtime description was unavailable.",
            limitations);

        return new LocalDeviceObservation(
            DateTimeOffset.UtcNow,
            limitations.Count == 0
                ? LocalDeviceObservationState.Current
                : LocalDeviceObservationState.Partial,
            DeviceEnrollmentState.NotEnrolled,
            displayName,
            operatingSystem,
            osArchitecture,
            processArchitecture,
            framework,
            "Environment.MachineName · RuntimeInformation",
            Array.AsReadOnly(limitations.ToArray()));
    }

    internal static string SanitizeField(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string sanitized = new string(value
            .Where(character => !char.IsControl(character))
            .Take(MaximumFieldLength)
            .ToArray()).Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static string ReadBounded(
        Func<string> read,
        string fallback,
        string limitation,
        List<string> limitations)
    {
        try
        {
            string rawValue = read();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                limitations.Add(limitation);
                return fallback;
            }

            return SanitizeField(rawValue, fallback);
        }
        catch (InvalidOperationException)
        {
            limitations.Add(limitation);
            return fallback;
        }
        catch (PlatformNotSupportedException)
        {
            limitations.Add(limitation);
            return fallback;
        }
    }
}
