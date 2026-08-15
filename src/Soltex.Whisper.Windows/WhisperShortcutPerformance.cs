using System.Diagnostics;

namespace Soltex.Whisper.Windows;

public sealed record WhisperShortcutPerformanceSnapshot(
    int SampleCount,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds);

internal sealed class WhisperShortcutPerformanceRecorder
{
    internal const int Capacity = 4096;

    private readonly long[] _elapsedTicks = new long[Capacity];
    private long _writeCount;

    internal void Record(long elapsedTicks)
    {
        long sequence = Interlocked.Increment(ref _writeCount) - 1;
        Volatile.Write(ref _elapsedTicks[(int)(sequence % Capacity)], Math.Max(1, elapsedTicks));
    }

    internal WhisperShortcutPerformanceSnapshot CreateSnapshot()
    {
        long observedWrites = Volatile.Read(ref _writeCount);
        int count = (int)Math.Min(observedWrites, Capacity);
        if (count == 0)
        {
            return new WhisperShortcutPerformanceSnapshot(0, 0, 0, 0);
        }

        long[] samples = new long[count];
        int start = observedWrites <= Capacity ? 0 : (int)(observedWrites % Capacity);
        for (int index = 0; index < count; index++)
        {
            samples[index] = Volatile.Read(ref _elapsedTicks[(start + index) % Capacity]);
        }

        samples = samples.Where(sample => sample > 0).ToArray();
        if (samples.Length == 0)
        {
            return new WhisperShortcutPerformanceSnapshot(0, 0, 0, 0);
        }

        Array.Sort(samples);
        return new WhisperShortcutPerformanceSnapshot(
            samples.Length,
            ToMicroseconds(Percentile(samples, 0.50)),
            ToMicroseconds(Percentile(samples, 0.95)),
            ToMicroseconds(Percentile(samples, 0.99)));
    }

    private static long Percentile(long[] sortedSamples, double percentile)
    {
        int index = (int)Math.Ceiling(sortedSamples.Length * percentile) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }

    private static double ToMicroseconds(long ticks) =>
        ticks * 1_000_000d / Stopwatch.Frequency;
}
