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

    internal static string SanitizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unavailable";
        }

        string safe = new(value
            .Where(character => !char.IsControl(character))
            .Take(SystemTelemetryProvider.MaximumProcessNameLength)
            .ToArray()).Trim();
        return safe.Length == 0 ? "Unavailable" : safe;
    }
}
