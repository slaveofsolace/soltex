using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soltex.App;

internal sealed record ActivityEntry(
    DateTimeOffset OccurredAtUtc,
    string Area,
    string Summary)
{
    internal static ActivityEntry Create(
        string? area,
        string? summary,
        DateTimeOffset occurredAtUtc)
    {
        string normalizedArea = NormalizeText(area, 32, "System");
        if (ContainsPathLikeText(normalizedArea))
        {
            normalizedArea = "System";
        }

        string normalizedSummary = NormalizeText(summary, 220, "Activity recorded.");
        if (ContainsPathLikeText(normalizedSummary))
        {
            normalizedSummary = string.Concat(
                normalizedArea,
                " activity referenced a local item.");
        }

        return new ActivityEntry(occurredAtUtc, normalizedArea, normalizedSummary);
    }

    private static string NormalizeText(string? value, int maximumLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(Math.Min(value.Length, maximumLength));
        bool pendingSpace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && builder.Length < maximumLength)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            if (builder.Length >= maximumLength)
            {
                break;
            }

            builder.Append(character);
        }

        string normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static bool ContainsPathLikeText(string value)
    {
        if (value.Contains(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        for (int index = 1; index < value.Length - 1; index++)
        {
            if (value[index] == ':' &&
                char.IsLetter(value[index - 1]) &&
                (index == 1 || !char.IsLetterOrDigit(value[index - 2])) &&
                (value[index + 1] == '\\' || value[index + 1] == '/'))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record ActivityLoadResult(
    IReadOnlyList<ActivityEntry> Entries,
    bool RecoveredFromInvalid,
    string Detail);

internal sealed record ActivityMutationResult(
    ActivityEntry? Entry,
    bool StorageHealthy,
    string Detail);

internal sealed class LocalActivityStore
{
    internal const int MaximumEntryCount = 120;
    internal const int MaximumFileBytes = 256 * 1_024;
    internal const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _filePath;
    private readonly List<ActivityEntry> _entries = [];

    internal LocalActivityStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal IReadOnlyList<ActivityEntry> Snapshot() => _entries.ToArray();

    internal ActivityLoadResult Load(ActivityRetention retention)
    {
        _entries.Clear();
        if (retention == ActivityRetention.SessionOnly)
        {
            return new ActivityLoadResult(
                Snapshot(),
                RecoveredFromInvalid: false,
                "Session only · nothing is kept after Soltex closes.");
        }

        if (!File.Exists(_filePath))
        {
            return new ActivityLoadResult(
                Snapshot(),
                RecoveredFromInvalid: false,
                RetentionDetail(retention));
        }

        try
        {
            FileInfo file = new(_filePath);
            if (file.Length > MaximumFileBytes)
            {
                return Recovered("Saved activity exceeded the bounded file size.");
            }

            string json = File.ReadAllText(_filePath, Encoding.UTF8);
            if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            {
                return Recovered("Saved activity exceeded the bounded file size.");
            }

            ActivityDocument? document =
                JsonSerializer.Deserialize<ActivityDocument>(json, SerializerOptions);
            if (document is null ||
                document.SchemaVersion != CurrentSchemaVersion ||
                document.Entries is null)
            {
                return Recovered("Saved activity uses an unsupported schema.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset cutoff = CutoffFor(retention, now);
            bool discarded = false;
            foreach (ActivityDocumentEntry item in document.Entries)
            {
                if (item.OccurredAtUtc == default ||
                    item.OccurredAtUtc > now.AddMinutes(5) ||
                    item.OccurredAtUtc < cutoff ||
                    string.IsNullOrWhiteSpace(item.Area) ||
                    string.IsNullOrWhiteSpace(item.Summary))
                {
                    discarded = true;
                    continue;
                }

                ActivityEntry entry =
                    ActivityEntry.Create(item.Area, item.Summary, item.OccurredAtUtc);
                _entries.Add(entry);
            }

            _entries.Sort(static (left, right) =>
                right.OccurredAtUtc.CompareTo(left.OccurredAtUtc));
            int countBeforeBound = _entries.Count;
            TrimToBound();
            discarded |= countBeforeBound != _entries.Count;
            if (discarded)
            {
                try
                {
                    Save();
                }
                catch (Exception exception) when (IsExpectedStorageFailure(exception))
                {
                    return new ActivityLoadResult(
                        Snapshot(),
                        RecoveredFromInvalid: true,
                        "Expired or unsupported entries were hidden, but saved cleanup could not finish.");
                }
            }

            return new ActivityLoadResult(
                Snapshot(),
                discarded,
                discarded
                    ? "Expired or unsupported activity was removed."
                    : RetentionDetail(retention));
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return Recovered("Saved activity could not be read; this session starts empty.");
        }
    }

    internal ActivityMutationResult Add(
        string area,
        string summary,
        ActivityRetention retention)
    {
        ActivityEntry entry = ActivityEntry.Create(area, summary, DateTimeOffset.UtcNow);
        _entries.Insert(0, entry);
        TrimToBound();

        if (retention == ActivityRetention.SessionOnly)
        {
            return new ActivityMutationResult(
                entry,
                StorageHealthy: true,
                "Session only · nothing is kept after Soltex closes.");
        }

        return TrySave(
            entry,
            RetentionDetail(retention),
            "Visible for this session, but saved activity could not be updated.");
    }

    internal ActivityMutationResult SetRetention(
        ActivityRetention retention,
        bool removePersistedWhenSessionOnly)
    {
        if (retention == ActivityRetention.SessionOnly)
        {
            if (!removePersistedWhenSessionOnly)
            {
                return new ActivityMutationResult(
                    null,
                    StorageHealthy: true,
                    "Session only · nothing is kept after Soltex closes.");
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                return new ActivityMutationResult(
                    null,
                    StorageHealthy: true,
                    "Session only · saved activity was removed.");
            }
            catch (Exception exception) when (IsExpectedStorageFailure(exception))
            {
                return new ActivityMutationResult(
                    null,
                    StorageHealthy: false,
                    "Session only is active, but the previous saved file could not be removed.");
            }
        }

        PruneForRetention(retention);
        return TrySave(
            null,
            RetentionDetail(retention),
            "Retention is active for this session, but activity could not be saved.");
    }

    internal ActivityMutationResult Clear()
    {
        _entries.Clear();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            return new ActivityMutationResult(
                null,
                StorageHealthy: true,
                "Activity is clear.");
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return new ActivityMutationResult(
                null,
                StorageHealthy: false,
                "The visible timeline is clear, but the saved file could not be removed.");
        }
    }

    private ActivityMutationResult TrySave(
        ActivityEntry? entry,
        string successDetail,
        string failureDetail)
    {
        try
        {
            Save();
            return new ActivityMutationResult(entry, StorageHealthy: true, successDetail);
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return new ActivityMutationResult(entry, StorageHealthy: false, failureDetail);
        }
    }

    private void Save()
    {
        ActivityDocument document = new(
            CurrentSchemaVersion,
            _entries.Select(static entry => new ActivityDocumentEntry(
                entry.OccurredAtUtc,
                entry.Area,
                entry.Summary)).ToArray());
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                "The activity document exceeded its size bound.");
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The activity file requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PruneForRetention(ActivityRetention retention)
    {
        DateTimeOffset cutoff = CutoffFor(retention, DateTimeOffset.UtcNow);
        _entries.RemoveAll(entry => entry.OccurredAtUtc < cutoff);
        TrimToBound();
    }

    private void TrimToBound()
    {
        if (_entries.Count > MaximumEntryCount)
        {
            _entries.RemoveRange(
                MaximumEntryCount,
                _entries.Count - MaximumEntryCount);
        }
    }

    private ActivityLoadResult Recovered(string detail)
    {
        _entries.Clear();
        return new ActivityLoadResult(
            Snapshot(),
            RecoveredFromInvalid: true,
            detail);
    }

    private static DateTimeOffset CutoffFor(
        ActivityRetention retention,
        DateTimeOffset now) =>
        retention switch
        {
            ActivityRetention.ThirtyDays => now.AddDays(-30),
            ActivityRetention.SevenDays => now.AddDays(-7),
            _ => DateTimeOffset.MinValue
        };

    private static string RetentionDetail(ActivityRetention retention) =>
        retention switch
        {
            ActivityRetention.ThirtyDays => "Saved on this Windows account for up to 30 days.",
            ActivityRetention.SevenDays => "Saved on this Windows account for up to 7 days.",
            _ => "Session only · nothing is kept after Soltex closes."
        };

    private static bool IsExpectedStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            JsonException or InvalidOperationException;

    private sealed record ActivityDocument(
        int SchemaVersion,
        IReadOnlyList<ActivityDocumentEntry>? Entries);

    private sealed record ActivityDocumentEntry(
        DateTimeOffset OccurredAtUtc,
        string Area,
        string Summary);
}
