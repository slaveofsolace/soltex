using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;

namespace Soltex.App;

internal enum TelemetryCadence
{
    Live,
    Balanced,
    Quiet
}

internal enum ActivityRetention
{
    SessionOnly,
    SevenDays,
    ThirtyDays
}

internal enum CloseBehavior
{
    Exit,
    NotificationArea
}

internal sealed record SoltexPreferences(
    TelemetryCadence TelemetryCadence,
    bool RestoreLastWorkspace,
    bool OpenPerformanceDetails,
    ActivityRetention ActivityRetention,
    CloseBehavior CloseBehavior,
    string LastWorkspace)
{
    internal const int CurrentSchemaVersion = 2;

    internal static SoltexPreferences Default { get; } =
        new(
            global::Soltex.App.TelemetryCadence.Balanced,
            true,
            false,
            global::Soltex.App.ActivityRetention.SessionOnly,
            global::Soltex.App.CloseBehavior.Exit,
            "home");

    internal int TelemetryIntervalMilliseconds => TelemetryCadence switch
    {
        global::Soltex.App.TelemetryCadence.Live => 1_000,
        global::Soltex.App.TelemetryCadence.Quiet => 5_000,
        _ => 2_000
    };

    internal SoltexPreferences Normalize() =>
        new(
            Enum.IsDefined(TelemetryCadence) ? TelemetryCadence : global::Soltex.App.TelemetryCadence.Balanced,
            RestoreLastWorkspace,
            OpenPerformanceDetails,
            Enum.IsDefined(ActivityRetention)
                ? ActivityRetention
                : global::Soltex.App.ActivityRetention.SessionOnly,
            Enum.IsDefined(CloseBehavior)
                ? CloseBehavior
                : global::Soltex.App.CloseBehavior.Exit,
            NormalizeWorkspace(LastWorkspace));

    internal static string NormalizeWorkspace(string? value)
    {
        string candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return candidate is
            "home" or
            "monitoring" or
            "applications" or
            "mixer" or
            "security" or
            "remote" or
            "activity" or
            "updates" or
            "settings"
                ? candidate
                : "home";
    }
}

internal sealed record PreferencesLoadResult(
    SoltexPreferences Preferences,
    bool RecoveredFromInvalid,
    string Detail);

internal sealed class PreferencesStore
{
    internal const int MaximumPreferenceBytes = 16 * 1_024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    internal PreferencesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal PreferencesLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new PreferencesLoadResult(
                SoltexPreferences.Default,
                RecoveredFromInvalid: false,
                "Defaults are active until a preference changes.");
        }

        try
        {
            FileInfo file = new(_filePath);
            if (file.Length > MaximumPreferenceBytes)
            {
                return Recovered("The saved preferences exceeded the bounded file size.");
            }

            string json = File.ReadAllText(_filePath, Encoding.UTF8);
            if (Encoding.UTF8.GetByteCount(json) > MaximumPreferenceBytes)
            {
                return Recovered("The saved preferences exceeded the bounded file size.");
            }

            PreferencesDocument? document =
                JsonSerializer.Deserialize<PreferencesDocument>(json, SerializerOptions);
            if (document is null || document.SchemaVersion is not (1 or SoltexPreferences.CurrentSchemaVersion))
            {
                return Recovered("The saved preferences use an unsupported schema.");
            }

            bool validCadence =
                Enum.TryParse(
                    document.TelemetryCadence,
                    ignoreCase: true,
                out TelemetryCadence cadence) &&
                Enum.IsDefined(cadence);
            bool retentionMissing = string.IsNullOrWhiteSpace(document.ActivityRetention);
            ActivityRetention retention = global::Soltex.App.ActivityRetention.SessionOnly;
            bool validRetention =
                retentionMissing ||
                (Enum.TryParse(
                    document.ActivityRetention,
                    ignoreCase: true,
                    out retention) &&
                 Enum.IsDefined(retention));
            bool closeBehaviorMissing = string.IsNullOrWhiteSpace(document.CloseBehavior);
            CloseBehavior closeBehavior = global::Soltex.App.CloseBehavior.Exit;
            bool validCloseBehavior =
                closeBehaviorMissing ||
                (Enum.TryParse(
                    document.CloseBehavior,
                    ignoreCase: true,
                    out closeBehavior) &&
                 Enum.IsDefined(closeBehavior));
            string workspace = SoltexPreferences.NormalizeWorkspace(document.LastWorkspace);
            bool normalized =
                !validCadence ||
                (!retentionMissing && !validRetention) ||
                (!closeBehaviorMissing && !validCloseBehavior) ||
                !string.Equals(
                    workspace,
                    document.LastWorkspace?.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            SoltexPreferences preferences = new(
                validCadence ? cadence : global::Soltex.App.TelemetryCadence.Balanced,
                document.RestoreLastWorkspace,
                document.OpenPerformanceDetails,
                validRetention && !retentionMissing
                    ? retention
                    : global::Soltex.App.ActivityRetention.SessionOnly,
                validCloseBehavior && !closeBehaviorMissing
                    ? closeBehavior
                    : global::Soltex.App.CloseBehavior.Exit,
                workspace);
            bool migrated =
                document.SchemaVersion == 1 || closeBehaviorMissing;
            return new PreferencesLoadResult(
                preferences,
                normalized,
                normalized
                    ? "Unsupported preference values were reset to safe defaults."
                    : migrated
                        ? "Preferences loaded; close behavior remains Exit until explicitly changed."
                    : "Preferences loaded from this Windows account.");
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return Recovered("The saved preferences could not be read; defaults are active.");
        }
    }

    internal void Save(SoltexPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        SoltexPreferences normalized = preferences.Normalize();
        PreferencesDocument document = new(
            SoltexPreferences.CurrentSchemaVersion,
            normalized.TelemetryCadence.ToString(),
            normalized.RestoreLastWorkspace,
            normalized.OpenPerformanceDetails,
            normalized.ActivityRetention.ToString(),
            normalized.CloseBehavior.ToString(),
            normalized.LastWorkspace);
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPreferenceBytes)
        {
            throw new InvalidOperationException("The preference document exceeded its size bound.");
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The preference file requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

    private static PreferencesLoadResult Recovered(string detail) =>
        new(SoltexPreferences.Default, RecoveredFromInvalid: true, detail);

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or JsonException;

    private sealed record PreferencesDocument(
        int SchemaVersion,
        string TelemetryCadence,
        bool RestoreLastWorkspace,
        bool OpenPerformanceDetails,
        string? ActivityRetention,
        string? CloseBehavior,
        string LastWorkspace);
}
