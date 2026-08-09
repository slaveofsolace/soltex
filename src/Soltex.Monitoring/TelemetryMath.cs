namespace Soltex.Monitoring;

internal static class TelemetryMath
{
    internal static double? CalculateSystemUsage(
        ulong firstIdle,
        ulong firstKernel,
        ulong firstUser,
        ulong secondIdle,
        ulong secondKernel,
        ulong secondUser)
    {
        if (secondIdle < firstIdle || secondKernel < firstKernel || secondUser < firstUser)
        {
            return null;
        }

        ulong idleDelta = secondIdle - firstIdle;
        ulong kernelDelta = secondKernel - firstKernel;
        ulong userDelta = secondUser - firstUser;
        ulong totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return null;
        }

        return Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100, 0, 100);
    }

    internal static double CalculateProcessUsage(
        TimeSpan firstProcessorTime,
        TimeSpan secondProcessorTime,
        TimeSpan elapsed,
        int processorCount)
    {
        if (secondProcessorTime < firstProcessorTime ||
            elapsed <= TimeSpan.Zero ||
            processorCount <= 0)
        {
            return 0;
        }

        double capacityMilliseconds = elapsed.TotalMilliseconds * processorCount;
        if (capacityMilliseconds <= 0)
        {
            return 0;
        }

        double usedMilliseconds = (secondProcessorTime - firstProcessorTime).TotalMilliseconds;
        return Math.Clamp(usedMilliseconds / capacityMilliseconds * 100, 0, 100);
    }

    internal static long? CalculateByteRate(
        long firstBytes,
        long secondBytes,
        TimeSpan elapsed)
    {
        if (firstBytes < 0 || secondBytes < firstBytes || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        double rate = (secondBytes - firstBytes) / elapsed.TotalSeconds;
        if (!double.IsFinite(rate) || rate < 0)
        {
            return null;
        }

        return rate >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Round(rate, MidpointRounding.AwayFromZero);
    }

    internal static string SanitizeProcessName(string? value) =>
        SanitizeLabel(value, SystemTelemetryProvider.MaximumProcessNameLength);

    internal static string SanitizeNetworkName(string? value) =>
        SanitizeLabel(value, SystemTelemetryProvider.MaximumNetworkNameLength);

    private static string SanitizeLabel(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unavailable";
        }

        string safe = new string(value
            .Where(character => !char.IsControl(character))
            .Take(maximumLength)
            .ToArray()).Trim();
        return safe.Length == 0 ? "Unavailable" : safe;
    }
}
