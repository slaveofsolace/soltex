using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using Soltex.Audio;

namespace Soltex.App;

internal sealed record AudioMixEntry(
    string ApplicationName,
    string EndpointName,
    double VolumePercent,
    bool IsMuted);

internal sealed record AudioMixSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<AudioMixEntry> Entries);

internal sealed record AudioMixCaptureResult(
    AudioMixSnapshot? Snapshot,
    int SkippedUncontrollable,
    int SkippedAmbiguous,
    int OmittedByBound);

internal sealed record AudioMixMatch(
    AudioMixEntry Entry,
    AudioSession Session);

internal sealed record AudioMixApplyPlan(
    IReadOnlyList<AudioMixMatch> Matches,
    int MissingCount,
    int AmbiguousCount);

internal sealed record AudioMixLoadResult(
    AudioMixSnapshot? Snapshot,
    bool RecoveredFromInvalid,
    string Detail);

internal static class AudioMixSnapshotPlanner
{
    internal const int MaximumEntries = 64;
    internal const int MaximumApplicationNameLength = 128;
    internal const int MaximumEndpointNameLength = 256;

    internal static AudioMixCaptureResult Capture(IEnumerable<AudioSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        AudioSession[] observed = sessions.ToArray();
        int uncontrollable = observed.Count(session => !session.CanControl);
        AudioSession[] controllable = observed.Where(session => session.CanControl).ToArray();
        var groups = controllable
            .Where(session => TryNormalizeLabel(
                session.Name,
                MaximumApplicationNameLength,
                out _) && TryNormalizeLabel(
                session.EndpointName,
                MaximumEndpointNameLength,
                out _))
            .GroupBy(SessionKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int ambiguous = groups.Where(group => group.Count() != 1).Sum(group => group.Count());
        AudioMixEntry[] eligible = groups
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Select(session => new AudioMixEntry(
                session.Name.Trim(),
                session.EndpointName.Trim(),
                Math.Round(session.VolumePercent, 1, MidpointRounding.AwayFromZero),
                session.IsMuted))
            .OrderBy(entry => entry.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.EndpointName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        int omitted = Math.Max(0, eligible.Length - MaximumEntries);
        AudioMixEntry[] bounded = eligible.Take(MaximumEntries).ToArray();
        AudioMixSnapshot? snapshot = bounded.Length == 0
            ? null
            : new AudioMixSnapshot(
                DateTimeOffset.UtcNow,
                Array.AsReadOnly(bounded));
        return new AudioMixCaptureResult(snapshot, uncontrollable, ambiguous, omitted);
    }

    internal static AudioMixApplyPlan Plan(
        AudioMixSnapshot snapshot,
        IEnumerable<AudioSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sessions);
        AudioSession[] live = sessions.Where(session => session.CanControl).ToArray();
        List<AudioMixMatch> matches = [];
        int missing = 0;
        int ambiguous = 0;
        foreach (AudioMixEntry entry in snapshot.Entries.Take(MaximumEntries))
        {
            AudioSession[] candidates = live.Where(session =>
                string.Equals(
                    session.Name.Trim(),
                    entry.ApplicationName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    session.EndpointName.Trim(),
                    entry.EndpointName,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 1)
            {
                matches.Add(new AudioMixMatch(entry, candidates[0]));
            }
            else if (candidates.Length == 0)
            {
                missing++;
            }
            else
            {
                ambiguous++;
            }
        }

        return new AudioMixApplyPlan(matches.AsReadOnly(), missing, ambiguous);
    }

    internal static bool TryCreateEntry(
        string? applicationName,
        string? endpointName,
        double volumePercent,
        bool isMuted,
        out AudioMixEntry? entry)
    {
        entry = null;
        if (!TryNormalizeLabel(
                applicationName,
                MaximumApplicationNameLength,
                out string normalizedApplication) ||
            !TryNormalizeLabel(
                endpointName,
                MaximumEndpointNameLength,
                out string normalizedEndpoint) ||
            !double.IsFinite(volumePercent) ||
            volumePercent is < 0 or > 100)
        {
            return false;
        }

        entry = new AudioMixEntry(
            normalizedApplication,
            normalizedEndpoint,
            Math.Round(volumePercent, 1, MidpointRounding.AwayFromZero),
            isMuted);
        return true;
    }

    private static string SessionKey(AudioSession session) =>
        session.Name.Trim() + "\u001f" + session.EndpointName.Trim();

    private static bool TryNormalizeLabel(
        string? value,
        int maximumLength,
        out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 &&
            normalized.Length <= maximumLength &&
            !normalized.Any(char.IsControl);
    }
}

internal sealed class AudioMixSnapshotStore
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumDocumentBytes = 64 * 1_024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    internal AudioMixSnapshotStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal AudioMixLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AudioMixLoadResult(
                null,
                RecoveredFromInvalid: false,
                "No mix snapshot has been captured.");
        }

        try
        {
            FileInfo info = new(_filePath);
            if (info.Length > MaximumDocumentBytes)
            {
                return Recovered("The saved mix snapshot exceeded its size bound and was not loaded.");
            }

            string json = File.ReadAllText(_filePath, Encoding.UTF8);
            if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            {
                return Recovered("The saved mix snapshot exceeded its size bound and was not loaded.");
            }

            AudioMixDocument? document =
                JsonSerializer.Deserialize<AudioMixDocument>(json, SerializerOptions);
            if (document is null ||
                document.SchemaVersion != CurrentSchemaVersion ||
                document.CapturedAtUtc == default ||
                document.Entries is null ||
                document.Entries.Count is < 1 or > AudioMixSnapshotPlanner.MaximumEntries)
            {
                return Recovered("The saved mix snapshot was invalid and was not loaded.");
            }

            List<AudioMixEntry> entries = [];
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            foreach (AudioMixEntryDocument item in document.Entries)
            {
                if (!AudioMixSnapshotPlanner.TryCreateEntry(
                        item.ApplicationName,
                        item.EndpointName,
                        item.VolumePercent,
                        item.IsMuted,
                        out AudioMixEntry? entry) ||
                    entry is null ||
                    !string.Equals(entry.ApplicationName, item.ApplicationName, StringComparison.Ordinal) ||
                    !string.Equals(entry.EndpointName, item.EndpointName, StringComparison.Ordinal) ||
                    !keys.Add(entry.ApplicationName + "\u001f" + entry.EndpointName))
                {
                    return Recovered("The saved mix snapshot contained an invalid or ambiguous entry and was not loaded.");
                }

                entries.Add(entry);
            }

            AudioMixSnapshot snapshot = new(
                document.CapturedAtUtc,
                Array.AsReadOnly(entries.ToArray()));
            return new AudioMixLoadResult(
                snapshot,
                RecoveredFromInvalid: false,
                Describe(snapshot));
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return Recovered("The saved mix snapshot could not be read; no Audio controls were changed.");
        }
    }

    internal void Save(AudioMixSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.CapturedAtUtc == default ||
            snapshot.Entries.Count is < 1 or > AudioMixSnapshotPlanner.MaximumEntries)
        {
            throw new InvalidOperationException("The mix snapshot is outside its entry bound.");
        }

        List<AudioMixEntryDocument> entries = [];
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (AudioMixEntry item in snapshot.Entries)
        {
            if (!AudioMixSnapshotPlanner.TryCreateEntry(
                    item.ApplicationName,
                    item.EndpointName,
                    item.VolumePercent,
                    item.IsMuted,
                    out AudioMixEntry? normalized) ||
                normalized is null ||
                !keys.Add(normalized.ApplicationName + "\u001f" + normalized.EndpointName))
            {
                throw new InvalidOperationException("The mix snapshot contained an invalid or ambiguous entry.");
            }

            entries.Add(new AudioMixEntryDocument(
                normalized.ApplicationName,
                normalized.EndpointName,
                normalized.VolumePercent,
                normalized.IsMuted));
        }

        AudioMixDocument document = new(
            CurrentSchemaVersion,
            snapshot.CapturedAtUtc,
            entries);
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The mix snapshot exceeded its document-size bound.");
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The mix snapshot requires a parent directory.");
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

    internal void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    internal static string Describe(AudioMixSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string count = snapshot.Entries.Count == 1
            ? "1 app session"
            : $"{snapshot.Entries.Count} app sessions";
        return $"{count} captured {snapshot.CapturedAtUtc.ToLocalTime():g}.";
    }

    private static AudioMixLoadResult Recovered(string detail) =>
        new(null, RecoveredFromInvalid: true, detail);

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or JsonException;

    private sealed record AudioMixDocument(
        int SchemaVersion,
        DateTimeOffset CapturedAtUtc,
        List<AudioMixEntryDocument>? Entries);

    private sealed record AudioMixEntryDocument(
        string? ApplicationName,
        string? EndpointName,
        double VolumePercent,
        bool IsMuted);
}
