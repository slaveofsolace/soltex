using System.Collections.ObjectModel;

namespace Soltex.Monitoring;

public sealed class BoundedTelemetryHistory
{
    public const int MinimumCapacity = 2;
    public const int MaximumCapacity = 120;
    private readonly object _sync = new();
    private readonly Queue<double> _samples = [];

    public BoundedTelemetryHistory(int capacity)
    {
        if (capacity is < MinimumCapacity or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                $"History capacity must be between {MinimumCapacity} and {MaximumCapacity} samples.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public ReadOnlyCollection<double> Add(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Telemetry history accepts only finite values.");
        }

        lock (_sync)
        {
            _samples.Enqueue(Math.Clamp(value, 0, 100));
            while (_samples.Count > Capacity)
            {
                _samples.Dequeue();
            }

            return Array.AsReadOnly(_samples.ToArray());
        }
    }

    public ReadOnlyCollection<double> CreateSnapshot()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_samples.ToArray());
        }
    }
}
