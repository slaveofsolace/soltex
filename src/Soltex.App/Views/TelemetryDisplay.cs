using System.Globalization;
using Soltex.Monitoring;

namespace Soltex.App.Views;

internal static class TelemetryDisplay
{
    internal static string Percent(double? value) =>
        value is null ? "—" : value.Value.ToString("F0", CultureInfo.CurrentCulture) + "%";

    internal static string Bytes(ulong bytes) => BytesCore(bytes);

    internal static string Bytes(long bytes) => BytesCore(Math.Max(0, bytes));

    internal static string BytesPerSecond(long bytesPerSecond) =>
        BytesCore(Math.Max(0, bytesPerSecond)) + "/s";

    internal static string State(TelemetryObservationState state) => state switch
    {
        TelemetryObservationState.Current => "CURRENT",
        TelemetryObservationState.Partial => "PARTIAL",
        TelemetryObservationState.Stale => "STALE",
        _ => "UNAVAILABLE"
    };

    private static string BytesCore(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string format = unit <= 1 ? "F0" : "F1";
        return bytes.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
