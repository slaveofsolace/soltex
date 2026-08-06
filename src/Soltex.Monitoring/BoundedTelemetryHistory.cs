using System.Collections.ObjectModel;

namespace Soltex.Monitoring;

public sealed class BoundedTelemetryHistory
{
    public const int MinimumCapacity = 2;
    public const int MaximumCapacity = 120;
    private readonly object _sync = new();
    private readonly Queue<double> _samples = [];
    private readonly double _minimum;
    private readonly double _maximum;

    public BoundedTelemetryHistory(
        int capacity,
        double minimum = 0,
        double maximum = 100)
    {
        if (capacity is < MinimumCapacity or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                $"History capacity must be between {MinimumCapacity} and {MaximumCapacity} samples.");
        }

        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            maximum <= minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Telemetry history requires a finite minimum and a greater maximum.");
        }

        Capacity = capacity;
        _minimum = minimum;
        _maximum = maximum;
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
            _samples.Enqueue(Math.Clamp(value, _minimum, _maximum));
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
