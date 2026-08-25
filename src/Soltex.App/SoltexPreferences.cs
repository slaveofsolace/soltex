using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using Soltex.Security;

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

internal enum AppearancePreference
{
    System,
    Dark,
    Light
}

internal sealed record SoltexPreferences(
    TelemetryCadence TelemetryCadence,
    bool RestoreLastWorkspace,
    bool OpenPerformanceDetails,
    ActivityRetention ActivityRetention,
    CloseBehavior CloseBehavior,
    string PreferredPlaybackEndpointKey,
    string PreferredRecordingEndpointKey,
    string LastWorkspace,
    AppearancePreference AppearancePreference = global::Soltex.App.AppearancePreference.System,
    ThemeAccent ThemeAccent = global::Soltex.App.ThemeAccent.SoltexGlacier,
    InterfaceDensity InterfaceDensity = global::Soltex.App.InterfaceDensity.Comfortable,
    OnboardingState OnboardingState = default)
{
    internal const int CurrentSchemaVersion = 5;

    internal static SoltexPreferences Default { get; } =
        new(
            global::Soltex.App.TelemetryCadence.Balanced,
            true,
            false,
            global::Soltex.App.ActivityRetention.SessionOnly,
            global::Soltex.App.CloseBehavior.Exit,
            string.Empty,
            string.Empty,
            "home",
            global::Soltex.App.AppearancePreference.System,
            global::Soltex.App.ThemeAccent.SoltexGlacier,
            global::Soltex.App.InterfaceDensity.Comfortable,
            global::Soltex.App.OnboardingState.Default);

    internal ThemeProfile ThemeProfile =>
        new(
            AppearancePreference switch
            {
                global::Soltex.App.AppearancePreference.Light => ThemeMode.Light,
                global::Soltex.App.AppearancePreference.Dark => ThemeMode.Dark,
                _ => ThemeMode.System
            },
            ThemeAccent,
            InterfaceDensity);

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
            NormalizeEndpointPreferenceKey(PreferredPlaybackEndpointKey),
            NormalizeEndpointPreferenceKey(PreferredRecordingEndpointKey),
            NormalizeWorkspace(LastWorkspace),
            Enum.IsDefined(AppearancePreference)
                ? AppearancePreference
                : global::Soltex.App.AppearancePreference.System,
            Enum.IsDefined(ThemeAccent)
                ? ThemeAccent
                : global::Soltex.App.ThemeAccent.SoltexGlacier,
            Enum.IsDefined(InterfaceDensity)
                ? InterfaceDensity
                : global::Soltex.App.InterfaceDensity.Comfortable,
            OnboardingState == default
                ? global::Soltex.App.OnboardingState.Default
                : OnboardingState.Normalize());

    internal static string NormalizeEndpointPreferenceKey(string? value)
    {
        string candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return candidate.Length == 64 && candidate.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
                ? candidate
                : string.Empty;
    }

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
            "whisper" or
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

    internal static PreferencesStore CreateDefault()
    {
        ProductDataRootResolution resolution = ProductDataRootResolver.ResolveDefault();
        return new PreferencesStore(
            Path.Combine(resolution.ProductRoot, "Security", "preferences.json"));
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
            if (document is null ||
                document.SchemaVersion < 1 ||
                document.SchemaVersion > SoltexPreferences.CurrentSchemaVersion)
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
            bool appearanceMissing = string.IsNullOrWhiteSpace(document.AppearancePreference);
            AppearancePreference appearance = global::Soltex.App.AppearancePreference.System;
            bool validAppearance =
                appearanceMissing ||
                (Enum.TryParse(
                    document.AppearancePreference,
                    ignoreCase: true,
                    out appearance) &&
                 Enum.IsDefined(appearance));
            bool accentMissing = string.IsNullOrWhiteSpace(document.ThemeAccent);
            ThemeAccent themeAccent = global::Soltex.App.ThemeAccent.SoltexGlacier;
            bool validAccent =
                accentMissing ||
                (Enum.TryParse(
                    document.ThemeAccent,
                    ignoreCase: true,
                    out themeAccent) &&
                 Enum.IsDefined(themeAccent));
            bool densityMissing = string.IsNullOrWhiteSpace(document.InterfaceDensity);
            InterfaceDensity interfaceDensity = global::Soltex.App.InterfaceDensity.Comfortable;
            bool validDensity =
                densityMissing ||
                (Enum.TryParse(
                    document.InterfaceDensity,
                    ignoreCase: true,
                    out interfaceDensity) &&
                 Enum.IsDefined(interfaceDensity));
            bool onboardingMissing =
                document.OnboardingContractVersion is null ||
                document.OnboardingCompletedAreas is null ||
                document.OnboardingCompleted is null;
            OnboardingArea completedAreas = document.OnboardingCompletedAreas is int areasValue
                ? (OnboardingArea)areasValue
                : OnboardingArea.None;
            bool validOnboarding =
                onboardingMissing ||
                (document.OnboardingContractVersion == OnboardingState.CurrentContractVersion &&
                 (completedAreas & ~OnboardingArea.All) == 0 &&
                 (document.OnboardingCompleted != true ||
                  (completedAreas == OnboardingArea.All &&
                   document.OnboardingCompletedAtUtc is not null)));
            OnboardingState onboarding = validOnboarding && !onboardingMissing
                ? new OnboardingState(
                    OnboardingState.CurrentContractVersion,
                    completedAreas,
                    document.OnboardingCompleted!.Value,
                    document.OnboardingCompletedAtUtc).Normalize()
                : OnboardingState.Default;
            string workspace = SoltexPreferences.NormalizeWorkspace(document.LastWorkspace);
            string preferredPlayback =
                SoltexPreferences.NormalizeEndpointPreferenceKey(document.PreferredPlaybackEndpointKey);
            string preferredRecording =
                SoltexPreferences.NormalizeEndpointPreferenceKey(document.PreferredRecordingEndpointKey);
            bool normalized =
                !validCadence ||
                (!retentionMissing && !validRetention) ||
                (!closeBehaviorMissing && !validCloseBehavior) ||
                (!appearanceMissing && !validAppearance) ||
                (!accentMissing && !validAccent) ||
                (!densityMissing && !validDensity) ||
                (!onboardingMissing && !validOnboarding) ||
                (!string.IsNullOrWhiteSpace(document.PreferredPlaybackEndpointKey) &&
                 !string.Equals(
                     preferredPlayback,
                     document.PreferredPlaybackEndpointKey.Trim(),
                     StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(document.PreferredRecordingEndpointKey) &&
                 !string.Equals(
                     preferredRecording,
                     document.PreferredRecordingEndpointKey.Trim(),
                     StringComparison.OrdinalIgnoreCase)) ||
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
                preferredPlayback,
                preferredRecording,
                workspace,
                validAppearance && !appearanceMissing
                    ? appearance
                    : global::Soltex.App.AppearancePreference.System,
                validAccent && !accentMissing
                    ? themeAccent
                    : global::Soltex.App.ThemeAccent.SoltexGlacier,
                validDensity && !densityMissing
                    ? interfaceDensity
                    : global::Soltex.App.InterfaceDensity.Comfortable,
                onboarding);
            bool migrated =
                document.SchemaVersion < SoltexPreferences.CurrentSchemaVersion ||
                closeBehaviorMissing ||
                appearanceMissing ||
                accentMissing ||
                densityMissing ||
                onboardingMissing;
            return new PreferencesLoadResult(
                preferences,
                normalized,
                normalized
                    ? "Unsupported preference values were reset to safe defaults."
                    : migrated
                        ? "Settings loaded. New options use their recommended defaults."
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
            normalized.PreferredPlaybackEndpointKey,
            normalized.PreferredRecordingEndpointKey,
            normalized.LastWorkspace,
            normalized.AppearancePreference.ToString(),
            normalized.ThemeAccent.ToString(),
            normalized.InterfaceDensity.ToString(),
            normalized.OnboardingState.ContractVersion,
            (int)normalized.OnboardingState.CompletedAreas,
            normalized.OnboardingState.IsCompleted,
            normalized.OnboardingState.CompletedAtUtc);
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
        string? PreferredPlaybackEndpointKey,
        string? PreferredRecordingEndpointKey,
        string LastWorkspace,
        string? AppearancePreference,
        string? ThemeAccent = null,
        string? InterfaceDensity = null,
        int? OnboardingContractVersion = null,
        int? OnboardingCompletedAreas = null,
        bool? OnboardingCompleted = null,
        DateTimeOffset? OnboardingCompletedAtUtc = null);
}
