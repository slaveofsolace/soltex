using System.Collections.ObjectModel;

namespace Soltex.Whisper;

public sealed class BoundedWhisperHistory
{
    private readonly object _sync = new();
    private readonly Queue<WhisperHistoryEntry> _entries = [];

    public BoundedWhisperHistory(int capacity)
    {
        if (capacity is < 1 or > WhisperLimits.MaximumHistoryEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                $"History capacity must be between 1 and {WhisperLimits.MaximumHistoryEntries} entries.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<WhisperHistoryEntry> Add(
        WhisperHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Text);
        ArgumentNullException.ThrowIfNull(entry.ProcessName);

        if (entry.Text.Length > WhisperLimits.MaximumTranscriptCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entry),
                $"History text cannot exceed {WhisperLimits.MaximumTranscriptCharacters} characters.");
        }

        if (string.IsNullOrWhiteSpace(entry.ProcessName) ||
            entry.ProcessName.Length > WhisperLimits.MaximumProcessNameCharacters ||
            entry.ProcessName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "History process names must contain 1 to 128 printable characters.",
                nameof(entry));
        }

        lock (_sync)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }

            return Array.AsReadOnly(_entries.ToArray());
        }
    }

    public ReadOnlyCollection<WhisperHistoryEntry> CreateSnapshot()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_entries.ToArray());
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }
}
