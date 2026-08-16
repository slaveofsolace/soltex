using System.Collections.ObjectModel;

namespace Soltex.Whisper;

/// <summary>
/// Where completed transcripts live.
/// </summary>
public enum WhisperHistoryMode
{
    /// <summary>Nothing is kept beyond the current transcript.</summary>
    Off,

    /// <summary>Bounded in-memory history, cleared when Soltex exits. The default.</summary>
    SessionMemory,

    /// <summary>Opt-in encrypted retention through the Soltex authenticated state boundary.</summary>
    EncryptedDisk
}

/// <summary>
/// What Whisper does with a clipboard it had to borrow for a paste fallback.
/// </summary>
public enum WhisperClipboardBehavior
{
    /// <summary>Leave the transcript on the clipboard.</summary>
    LeaveTranscript,

    /// <summary>Restore the previous contents, but only while Whisper still owns the sequence.</summary>
    RestorePrevious
}

/// <summary>
/// The mutable, possibly-invalid shape read from storage. Every field is nullable so a
/// truncated or hand-edited file can be inspected rather than throwing on load.
/// </summary>
public sealed class WhisperSettingsDocument
{
    public int? Version { get; set; }

    public bool? Enabled { get; set; }

    public string? InputDeviceId { get; set; }

    public string? TranscriberId { get; set; }

    public string? PreferredLanguageTag { get; set; }

    public string? DefaultStyleName { get; set; }

    public bool? AutoSendEnabled { get; set; }

    public bool? AutoSendWarningAccepted { get; set; }

    public bool? ContextReadsAllowed { get; set; }

    public string? HistoryMode { get; set; }

    public int? HistoryRetentionDays { get; set; }

    public string? ClipboardBehavior { get; set; }

    public bool? ShowTranscriptPreview { get; set; }

    /// <summary>
    /// Exposed as a read-only list so the stored shape cannot be mutated in place by a
    /// caller holding the document, and so it is not treated as a settable collection.
    /// </summary>
    public IReadOnlyList<string>? VocabularyTerms { get; set; }

    public IReadOnlyList<WhisperSnippetDocument>? Snippets { get; set; }

    public IReadOnlyList<WhisperStyleProfileDocument>? CustomStyles { get; set; }

    public IReadOnlyList<WhisperAppProfileDocument>? ApplicationProfiles { get; set; }
}

public sealed class WhisperSnippetDocument
{
    public string? Cue { get; set; }

    public string? Content { get; set; }
}

public sealed class WhisperStyleProfileDocument
{
    public string? Name { get; set; }

    public string? Kind { get; set; }

    public bool? ProseCleanup { get; set; }

    public bool? SpokenPunctuation { get; set; }

    public bool? PreserveLiteralTokens { get; set; }

    public bool? CapitalizeSentences { get; set; }
}

public sealed class WhisperAppProfileDocument
{
    public string? ProcessName { get; set; }

    public bool? AutoSendAllowed { get; set; }

    public bool? TerminalAutoSendAllowed { get; set; }

    public bool? ClipboardFallbackAllowed { get; set; }

    public bool? ContextFormattingAllowed { get; set; }

    public string? StyleName { get; set; }
}

/// <summary>
/// A validated settings document. Constructing one is the only way to get settings
/// Whisper will act on, so an invalid file cannot reach the runtime.
/// </summary>
public sealed class WhisperSettings
{
    public const int CurrentVersion = 3;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 90;
    public const int MaximumDeviceIdCharacters = 256;
    public const int MaximumStoredSnippetCount = 64;
    public const int MaximumStoredSnippetContentCharacters = 4_000;
    public const int MaximumCustomStyleCount = 16;
    public const int MaximumApplicationProfileCount = 64;

    private WhisperSettings(
        bool enabled,
        string? inputDeviceId,
        string? transcriberId,
        WhisperLanguageSelection language,
        string defaultStyleName,
        bool autoSendEnabled,
        bool autoSendWarningAccepted,
        bool contextReadsAllowed,
        WhisperHistoryMode historyMode,
        int historyRetentionDays,
        WhisperClipboardBehavior clipboardBehavior,
        bool showTranscriptPreview,
        WhisperVocabulary vocabulary,
        IEnumerable<WhisperSnippet> snippets,
        IEnumerable<WhisperStyleProfile> customStyles,
        IEnumerable<WhisperAppProfile> applicationProfiles)
    {
        Enabled = enabled;
        InputDeviceId = inputDeviceId;
        TranscriberId = transcriberId;
        Language = language;
        DefaultStyleName = defaultStyleName;
        AutoSendEnabled = autoSendEnabled;
        AutoSendWarningAccepted = autoSendWarningAccepted;
        ContextReadsAllowed = contextReadsAllowed;
        HistoryMode = historyMode;
        HistoryRetentionDays = historyRetentionDays;
        ClipboardBehavior = clipboardBehavior;
        ShowTranscriptPreview = showTranscriptPreview;
        Vocabulary = vocabulary;
        Snippets = Array.AsReadOnly(snippets.ToArray());
        CustomStyles = Array.AsReadOnly(customStyles.ToArray());
        ApplicationProfiles = Array.AsReadOnly(applicationProfiles.ToArray());
    }

    public bool Enabled { get; }

    public string? InputDeviceId { get; }

    public string? TranscriberId { get; }

    public WhisperLanguageSelection Language { get; }

    public string DefaultStyleName { get; }

    /// <summary>Off by default. Enter is never emitted while this is false.</summary>
    public bool AutoSendEnabled { get; }

    public bool AutoSendWarningAccepted { get; }

    /// <summary>Off by default. Governs reading text around the caret for context.</summary>
    public bool ContextReadsAllowed { get; }

    public WhisperHistoryMode HistoryMode { get; }

    public int HistoryRetentionDays { get; }

    public WhisperClipboardBehavior ClipboardBehavior { get; }

    public bool ShowTranscriptPreview { get; }

    public WhisperVocabulary Vocabulary { get; }

    public ReadOnlyCollection<WhisperSnippet> Snippets { get; }

    public ReadOnlyCollection<WhisperStyleProfile> CustomStyles { get; }

    public ReadOnlyCollection<WhisperAppProfile> ApplicationProfiles { get; }

    /// <summary>
    /// The shipped defaults. Every capability that can act outside Soltex — auto-send,
    /// context reads, disk retention — starts off.
    /// </summary>
    public static WhisperSettings CreateDefault() => new(
        enabled: false,
        inputDeviceId: null,
        transcriberId: null,
        WhisperLanguageSelection.AutoDetect,
        WhisperStyleProfile.Message.Name,
        autoSendEnabled: false,
        autoSendWarningAccepted: false,
        contextReadsAllowed: false,
        WhisperHistoryMode.SessionMemory,
        historyRetentionDays: 7,
        WhisperClipboardBehavior.RestorePrevious,
        showTranscriptPreview: false,
        WhisperVocabulary.Empty,
        [],
        [],
        []);

    internal static WhisperSettings Create(
        bool enabled,
        string? inputDeviceId,
        string? transcriberId,
        WhisperLanguageSelection language,
        string defaultStyleName,
        bool autoSendEnabled,
        bool autoSendWarningAccepted,
        bool contextReadsAllowed,
        WhisperHistoryMode historyMode,
        int historyRetentionDays,
        WhisperClipboardBehavior clipboardBehavior,
        bool showTranscriptPreview,
        WhisperVocabulary vocabulary,
        IEnumerable<WhisperSnippet> snippets,
        IEnumerable<WhisperStyleProfile> customStyles,
        IEnumerable<WhisperAppProfile> applicationProfiles) => new(
            enabled,
            inputDeviceId,
            transcriberId,
            language,
            defaultStyleName,
            autoSendEnabled,
            autoSendWarningAccepted,
            contextReadsAllowed,
            historyMode,
            historyRetentionDays,
        clipboardBehavior,
        showTranscriptPreview,
        vocabulary,
        snippets,
        customStyles,
        applicationProfiles);

    public WhisperSettingsDocument ToDocument() => new()
    {
        Version = CurrentVersion,
        Enabled = Enabled,
        InputDeviceId = InputDeviceId,
        TranscriberId = TranscriberId,
        PreferredLanguageTag = Language.LanguageTag,
        DefaultStyleName = DefaultStyleName,
        AutoSendEnabled = AutoSendEnabled,
        AutoSendWarningAccepted = AutoSendWarningAccepted,
        ContextReadsAllowed = ContextReadsAllowed,
        HistoryMode = HistoryMode.ToString(),
        HistoryRetentionDays = HistoryRetentionDays,
        ClipboardBehavior = ClipboardBehavior.ToString(),
        ShowTranscriptPreview = ShowTranscriptPreview,
        VocabularyTerms = Vocabulary.Terms.ToList(),
        Snippets = Snippets.Select(snippet => new WhisperSnippetDocument
        {
            Cue = snippet.Cue,
            Content = snippet.Content
        }).ToList(),
        CustomStyles = CustomStyles.Select(style => new WhisperStyleProfileDocument
        {
            Name = style.Name,
            Kind = style.Kind.ToString(),
            ProseCleanup = style.ProseCleanup,
            SpokenPunctuation = style.SpokenPunctuation,
            PreserveLiteralTokens = style.PreserveLiteralTokens,
            CapitalizeSentences = style.CapitalizeSentences
        }).ToList(),
        ApplicationProfiles = ApplicationProfiles.Select(profile => new WhisperAppProfileDocument
        {
            ProcessName = profile.ProcessName,
            AutoSendAllowed = profile.AutoSendAllowed,
            TerminalAutoSendAllowed = profile.TerminalAutoSendAllowed,
            ClipboardFallbackAllowed = profile.ClipboardFallbackAllowed,
            ContextFormattingAllowed = profile.ContextFormattingAllowed,
            StyleName = profile.StyleName
        }).ToList()
    };
}

/// <summary>
/// The outcome of loading settings: the usable value, plus every correction that was
/// applied so the UI can tell the user what changed instead of silently rewriting
/// their configuration.
/// </summary>
public sealed class WhisperSettingsLoadResult
{
    internal WhisperSettingsLoadResult(
        WhisperSettings settings,
        int loadedVersion,
        bool migrated,
        IEnumerable<string> corrections)
    {
        Settings = settings;
        LoadedVersion = loadedVersion;
        Migrated = migrated;
        Corrections = Array.AsReadOnly(corrections.ToArray());
    }

    public WhisperSettings Settings { get; }

    public int LoadedVersion { get; }

    public bool Migrated { get; }

    public ReadOnlyCollection<string> Corrections { get; }

    public bool IsClean => Corrections.Count == 0 && !Migrated;
}

/// <summary>
/// Validates and migrates a stored settings document.
/// </summary>
/// <remarks>
/// The rule throughout is repair-to-safe, never repair-to-convenient: an
/// unparseable field falls back to the default that does <em>less</em>, and the
/// correction is reported. A settings file damaged by a partial write can therefore
/// never turn auto-send on.
/// </remarks>
public static class WhisperSettingsMigrator
{
    public static WhisperSettingsLoadResult Load(WhisperSettingsDocument? document)
    {
        List<string> corrections = [];

        if (document is null)
        {
            return new WhisperSettingsLoadResult(
                WhisperSettings.CreateDefault(),
                loadedVersion: 0,
                migrated: false,
                ["No settings document was found; shipped defaults were used."]);
        }

        int loadedVersion = document.Version ?? 0;
        if (loadedVersion is < 0 or > WhisperSettings.CurrentVersion)
        {
            corrections.Add(
                $"Settings version {loadedVersion} is not supported; shipped defaults were used.");
            return new WhisperSettingsLoadResult(
                WhisperSettings.CreateDefault(),
                loadedVersion,
                migrated: false,
                corrections);
        }

        bool enabled = document.Enabled ?? false;
        string? inputDeviceId = ReadBoundedText(
            document.InputDeviceId,
            WhisperSettings.MaximumDeviceIdCharacters,
            "Input device id",
            corrections);
        string? transcriberId = ReadBoundedText(
            document.TranscriberId,
            64,
            "Transcription provider id",
            corrections);

        WhisperLanguageSelection language = ReadLanguage(document.PreferredLanguageTag, corrections);
        string styleName = ReadStyleName(document.DefaultStyleName, corrections);

        bool autoSendEnabled = document.AutoSendEnabled ?? false;
        bool autoSendWarningAccepted = document.AutoSendWarningAccepted ?? false;
        if (autoSendEnabled && !autoSendWarningAccepted)
        {
            // Both halves of the consent must survive the round trip. A file that
            // claims auto-send without the accepted warning is treated as tampered.
            autoSendEnabled = false;
            corrections.Add("Auto-send was disabled because the first-use warning is not recorded as accepted.");
        }

        bool contextReads = document.ContextReadsAllowed ?? false;
        WhisperHistoryMode historyMode = ReadHistoryMode(document.HistoryMode, corrections);
        int retentionDays = ReadRetentionDays(document.HistoryRetentionDays, corrections);
        WhisperClipboardBehavior clipboard = ReadClipboardBehavior(document.ClipboardBehavior, corrections);
        bool preview = document.ShowTranscriptPreview ?? false;
        WhisperVocabulary vocabulary = ReadVocabulary(document.VocabularyTerms, corrections);
        ReadOnlyCollection<WhisperSnippet> snippets = ReadSnippets(document.Snippets, corrections);
        ReadOnlyCollection<WhisperStyleProfile> customStyles = ReadCustomStyles(
            document.CustomStyles,
            corrections);
        if (!WhisperStyleProfile.BuiltIn
                .Select(style => style.Name)
                .Concat(customStyles.Select(style => style.Name))
                .Contains(styleName, StringComparer.OrdinalIgnoreCase))
        {
            styleName = WhisperStyleProfile.Message.Name;
            corrections.Add("Default style was reset because the stored style no longer exists.");
        }

        ReadOnlyCollection<WhisperAppProfile> applicationProfiles = ReadApplicationProfiles(
            document.ApplicationProfiles,
            customStyles,
            corrections);

        // Version 1 had no explicit consent flag, so anything it recorded as enabled
        // has to be re-consented rather than inherited.
        bool migrated = loadedVersion < WhisperSettings.CurrentVersion;
        if (migrated && autoSendEnabled)
        {
            autoSendEnabled = false;
            autoSendWarningAccepted = false;
            corrections.Add(
                $"Auto-send consent was reset while migrating settings from version {loadedVersion}.");
        }

        WhisperSettings settings = WhisperSettings.Create(
            enabled,
            inputDeviceId,
            transcriberId,
            language,
            styleName,
            autoSendEnabled,
            autoSendWarningAccepted,
            contextReads,
            historyMode,
            retentionDays,
            clipboard,
            preview,
            vocabulary,
            snippets,
            customStyles,
            applicationProfiles);

        return new WhisperSettingsLoadResult(settings, loadedVersion, migrated, corrections);
    }

    private static string? ReadBoundedText(
        string? value,
        int maximumCharacters,
        string fieldName,
        List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maximumCharacters || trimmed.Any(char.IsControl))
        {
            corrections.Add($"{fieldName} was cleared because it was not a bounded printable value.");
            return null;
        }

        return trimmed;
    }

    private static WhisperLanguageSelection ReadLanguage(string? tag, List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return WhisperLanguageSelection.AutoDetect;
        }

        try
        {
            return WhisperLanguageSelection.Explicit(tag);
        }
        catch (ArgumentException)
        {
            corrections.Add($"Preferred language was reset to auto-detect because '{Preview(tag)}' is not a known language tag.");
            return WhisperLanguageSelection.AutoDetect;
        }
    }

    private static string ReadStyleName(string? styleName, List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            return WhisperStyleProfile.Message.Name;
        }

        string trimmed = styleName.Trim();
        if (trimmed.Length > WhisperStyleProfile.MaximumNameCharacters || trimmed.Any(char.IsControl))
        {
            corrections.Add("Default style was reset because the stored name was not a bounded printable value.");
            return WhisperStyleProfile.Message.Name;
        }

        return trimmed;
    }

    private static WhisperHistoryMode ReadHistoryMode(string? value, List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WhisperHistoryMode.SessionMemory;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out WhisperHistoryMode parsed) &&
            Enum.IsDefined(parsed))
        {
            if (parsed == WhisperHistoryMode.EncryptedDisk)
            {
                corrections.Add(
                    "Encrypted history was reset to session memory because encrypted retention is not available in this build.");
                return WhisperHistoryMode.SessionMemory;
            }

            return parsed;
        }

        corrections.Add("History mode was reset to session memory because the stored value was not recognized.");
        return WhisperHistoryMode.SessionMemory;
    }

    private static int ReadRetentionDays(int? value, List<string> corrections)
    {
        if (value is null)
        {
            return 7;
        }

        if (value.Value is < WhisperSettings.MinimumRetentionDays or > WhisperSettings.MaximumRetentionDays)
        {
            corrections.Add(
                $"History retention was reset to 7 days because {value.Value} is outside the supported range.");
            return 7;
        }

        return value.Value;
    }

    private static WhisperClipboardBehavior ReadClipboardBehavior(string? value, List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WhisperClipboardBehavior.RestorePrevious;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out WhisperClipboardBehavior parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        corrections.Add("Clipboard behaviour was reset to restoring the previous contents.");
        return WhisperClipboardBehavior.RestorePrevious;
    }

    private static WhisperVocabulary ReadVocabulary(IReadOnlyList<string>? terms, List<string> corrections)
    {
        if (terms is null || terms.Count == 0)
        {
            return WhisperVocabulary.Empty;
        }

        List<string> accepted = [];
        int rejected = 0;
        foreach (string term in terms)
        {
            try
            {
                accepted.Add(WhisperVocabulary.NormalizeTerm(term));
            }
            catch (ArgumentException)
            {
                rejected++;
            }
        }

        if (rejected > 0)
        {
            corrections.Add($"{rejected} dictionary term(s) were dropped because they were empty, oversized, or contained control characters.");
        }

        if (accepted.Count > WhisperVocabulary.MaximumEntries)
        {
            corrections.Add(
                $"The dictionary was truncated to the first {WhisperVocabulary.MaximumEntries} terms.");
            accepted = accepted.Take(WhisperVocabulary.MaximumEntries).ToList();
        }

        return new WhisperVocabulary(accepted);
    }

    private static ReadOnlyCollection<WhisperSnippet> ReadSnippets(
        IReadOnlyList<WhisperSnippetDocument>? documents,
        List<string> corrections)
    {
        if (documents is null || documents.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<WhisperSnippet>());
        }

        List<WhisperSnippet> accepted = [];
        HashSet<string> cues = new(StringComparer.OrdinalIgnoreCase);
        int rejected = 0;
        foreach (WhisperSnippetDocument document in documents.Take(WhisperSettings.MaximumStoredSnippetCount))
        {
            try
            {
                if (document.Cue is null || document.Content is null ||
                    document.Content.Length > WhisperSettings.MaximumStoredSnippetContentCharacters)
                {
                    throw new ArgumentException("Snippet fields were missing or oversized.");
                }

                WhisperSnippet snippet = new(document.Cue, document.Content);
                if (!cues.Add(snippet.Cue))
                {
                    throw new ArgumentException("Snippet cues must be unique.");
                }

                accepted.Add(snippet);
            }
            catch (ArgumentException)
            {
                rejected++;
            }
        }

        rejected += Math.Max(0, documents.Count - WhisperSettings.MaximumStoredSnippetCount);
        if (rejected > 0)
        {
            corrections.Add(
                $"{rejected} snippet(s) were dropped because they were duplicate, incomplete, or outside the stored size limits.");
        }

        return Array.AsReadOnly(accepted.ToArray());
    }

    private static ReadOnlyCollection<WhisperStyleProfile> ReadCustomStyles(
        IReadOnlyList<WhisperStyleProfileDocument>? documents,
        List<string> corrections)
    {
        if (documents is null || documents.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<WhisperStyleProfile>());
        }

        List<WhisperStyleProfile> accepted = [];
        HashSet<string> names = new(
            WhisperStyleProfile.BuiltIn.Select(style => style.Name),
            StringComparer.OrdinalIgnoreCase);
        int rejected = 0;
        foreach (WhisperStyleProfileDocument document in documents.Take(WhisperSettings.MaximumCustomStyleCount))
        {
            try
            {
                if (document.Name is null || document.Kind is null ||
                    !Enum.TryParse(document.Kind, ignoreCase: true, out WhisperStyleKind kind) ||
                    !Enum.IsDefined(kind) ||
                    document.ProseCleanup is null ||
                    document.SpokenPunctuation is null ||
                    document.PreserveLiteralTokens is null ||
                    document.CapitalizeSentences is null)
                {
                    throw new ArgumentException("Custom style fields were missing or invalid.");
                }

                WhisperStyleProfile style = new(
                    document.Name,
                    kind,
                    document.ProseCleanup.Value,
                    document.SpokenPunctuation.Value,
                    document.PreserveLiteralTokens.Value,
                    document.CapitalizeSentences.Value);
                if (!names.Add(style.Name))
                {
                    throw new ArgumentException("Custom style names must be unique.");
                }

                accepted.Add(style);
            }
            catch (ArgumentException)
            {
                rejected++;
            }
        }

        rejected += Math.Max(0, documents.Count - WhisperSettings.MaximumCustomStyleCount);
        if (rejected > 0)
        {
            corrections.Add(
                $"{rejected} custom style(s) were dropped because their name, kind, or visible rules were invalid.");
        }

        return Array.AsReadOnly(accepted.ToArray());
    }

    private static ReadOnlyCollection<WhisperAppProfile> ReadApplicationProfiles(
        IReadOnlyList<WhisperAppProfileDocument>? documents,
        IReadOnlyList<WhisperStyleProfile> customStyles,
        List<string> corrections)
    {
        if (documents is null || documents.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<WhisperAppProfile>());
        }

        HashSet<string> knownStyles = new(
            WhisperStyleProfile.BuiltIn.Select(style => style.Name)
                .Concat(customStyles.Select(style => style.Name)),
            StringComparer.OrdinalIgnoreCase);
        List<WhisperAppProfile> accepted = [];
        HashSet<string> processes = new(StringComparer.OrdinalIgnoreCase);
        int rejected = 0;
        foreach (WhisperAppProfileDocument document in documents.Take(WhisperSettings.MaximumApplicationProfileCount))
        {
            try
            {
                string styleName = string.IsNullOrWhiteSpace(document.StyleName)
                    ? WhisperStyleProfile.Message.Name
                    : document.StyleName;
                if (!knownStyles.Contains(styleName))
                {
                    throw new ArgumentException("Application profile style was not recognized.");
                }

                bool autoSendAllowed = document.AutoSendAllowed ?? false;
                WhisperAppProfile profile = new(
                    document.ProcessName ?? string.Empty,
                    autoSendAllowed,
                    terminalAutoSendAllowed: autoSendAllowed &&
                        (document.TerminalAutoSendAllowed ?? false),
                    clipboardFallbackAllowed: document.ClipboardFallbackAllowed ?? true,
                    contextFormattingAllowed: document.ContextFormattingAllowed ?? false,
                    styleName);
                if (!processes.Add(profile.ProcessName))
                {
                    throw new ArgumentException("Application process names must be unique.");
                }

                accepted.Add(profile);
            }
            catch (ArgumentException)
            {
                rejected++;
            }
        }

        rejected += Math.Max(0, documents.Count - WhisperSettings.MaximumApplicationProfileCount);
        if (rejected > 0)
        {
            corrections.Add(
                $"{rejected} application profile(s) were dropped because their process, style, or permissions were invalid.");
        }

        return Array.AsReadOnly(accepted.ToArray());
    }

    private static string Preview(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= 24 ? trimmed : trimmed[..24];
    }
}
