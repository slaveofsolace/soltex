using System.Collections.ObjectModel;
using System.Globalization;

namespace Soltex.Whisper;

/// <summary>
/// The user's personal vocabulary. Entries are passed to the transcriber as
/// recognition hints. They are deliberately never substituted into a finished
/// transcript, because blind post-transcription replacement corrupts homophones
/// and names the speaker did not say.
/// </summary>
public sealed class WhisperVocabulary
{
    public const int MaximumEntries = 512;
    public const int MaximumTermCharacters = 64;

    private readonly ReadOnlyCollection<string> _terms;

    public WhisperVocabulary(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        List<string> normalized = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string term in terms)
        {
            string candidate = NormalizeTerm(term);
            if (seen.Add(candidate))
            {
                normalized.Add(candidate);
            }
        }

        if (normalized.Count > MaximumEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terms),
                $"The personal dictionary cannot hold more than {MaximumEntries} entries.");
        }

        _terms = Array.AsReadOnly(normalized.ToArray());
    }

    public static WhisperVocabulary Empty { get; } = new(Array.Empty<string>());

    public ReadOnlyCollection<string> Terms => _terms;

    public bool Contains(string term)
    {
        return !string.IsNullOrWhiteSpace(term) &&
            _terms.Contains(term.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Collapses internal whitespace and rejects control characters and separators so a
    /// term can be sent as a hint without smuggling formatting into the provider request.
    /// </summary>
    internal static string NormalizeTerm(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        string collapsed = string.Join(
            ' ',
            term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (collapsed.Length is 0 or > MaximumTermCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(term),
                $"Dictionary terms must contain 1 to {MaximumTermCharacters} characters.");
        }

        if (collapsed.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Dictionary terms cannot contain control characters.",
                nameof(term));
        }

        return collapsed;
    }
}

/// <summary>
/// The broad shape of the text the user is dictating into. A style selects
/// formatting behaviour; it never selects a recipient and never carries a hidden
/// instruction the user cannot read in the UI.
/// </summary>
public enum WhisperStyleKind
{
    Message,
    Email,
    Document,
    Terminal,
    Developer
}

/// <summary>
/// A named, user-inspectable formatting profile. Every rule here maps to a visible
/// control; there are no concealed provider prompts.
/// </summary>
public sealed class WhisperStyleProfile
{
    public const int MaximumNameCharacters = 64;

    public WhisperStyleProfile(
        string name,
        WhisperStyleKind kind,
        bool proseCleanup,
        bool spokenPunctuation,
        bool preserveLiteralTokens,
        bool capitalizeSentences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameCharacters || normalizedName.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Style names must contain 1 to {MaximumNameCharacters} printable characters.");
        }

        Name = normalizedName;
        Kind = kind;
        ProseCleanup = proseCleanup;
        SpokenPunctuation = spokenPunctuation;
        PreserveLiteralTokens = preserveLiteralTokens;
        CapitalizeSentences = capitalizeSentences;
    }

    public string Name { get; }

    public WhisperStyleKind Kind { get; }

    /// <summary>Remove filler and tidy prose. Off for terminal and developer styles.</summary>
    public bool ProseCleanup { get; }

    /// <summary>Translate spoken punctuation words into characters.</summary>
    public bool SpokenPunctuation { get; }

    /// <summary>Keep paths, flags, identifiers, and casing exactly as transcribed.</summary>
    public bool PreserveLiteralTokens { get; }

    public bool CapitalizeSentences { get; }

    /// <summary>
    /// Projects this style onto the deterministic text pipeline. Terminal and developer
    /// styles keep the submit phrase active but turn prose rewriting off.
    /// </summary>
    public WhisperTextOptions ToTextOptions(bool expandSnippets = true)
    {
        return new WhisperTextOptions(
            SmartFormatting: SpokenPunctuation,
            Backtrack: ProseCleanup,
            ExpandSnippets: expandSnippets,
            DetectTerminalSubmit: true);
    }

    public static WhisperStyleProfile Message { get; } = new(
        "Message",
        WhisperStyleKind.Message,
        proseCleanup: true,
        spokenPunctuation: true,
        preserveLiteralTokens: false,
        capitalizeSentences: true);

    public static WhisperStyleProfile Email { get; } = new(
        "Email",
        WhisperStyleKind.Email,
        proseCleanup: true,
        spokenPunctuation: true,
        preserveLiteralTokens: false,
        capitalizeSentences: true);

    public static WhisperStyleProfile Document { get; } = new(
        "Document",
        WhisperStyleKind.Document,
        proseCleanup: true,
        spokenPunctuation: true,
        preserveLiteralTokens: false,
        capitalizeSentences: true);

    public static WhisperStyleProfile Terminal { get; } = new(
        "Terminal",
        WhisperStyleKind.Terminal,
        proseCleanup: false,
        spokenPunctuation: false,
        preserveLiteralTokens: true,
        capitalizeSentences: false);

    public static WhisperStyleProfile Developer { get; } = new(
        "Developer",
        WhisperStyleKind.Developer,
        proseCleanup: false,
        spokenPunctuation: true,
        preserveLiteralTokens: true,
        capitalizeSentences: false);

    public static ReadOnlyCollection<WhisperStyleProfile> BuiltIn { get; } =
        Array.AsReadOnly(new[] { Message, Email, Document, Terminal, Developer });

    /// <summary>
    /// Resolves the style for a session. Precedence is exact per-application profile,
    /// then the target category, then the general default. A terminal target never
    /// silently receives a prose style.
    /// </summary>
    public static WhisperStyleProfile Resolve(
        WhisperTargetContext target,
        WhisperAppProfile? appProfile,
        IEnumerable<WhisperStyleProfile>? customStyles = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Kind == WhisperTargetKind.Terminal)
        {
            return Terminal;
        }

        if (appProfile is not null && appProfile.MatchesProcess(target.ProcessName))
        {
            WhisperStyleProfile[] candidates = customStyles?.ToArray() ?? [];
            WhisperStyleProfile? named = candidates
                .Concat(BuiltIn)
                .FirstOrDefault(style => string.Equals(
                    style.Name,
                    appProfile.StyleName,
                    StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return named;
            }
        }

        return Message;
    }
}

/// <summary>
/// Language selection for a session. Auto-detect defers to the provider; an explicit
/// choice is passed through unchanged so mixed-language speech is not forced into one
/// language by Soltex.
/// </summary>
public sealed class WhisperLanguageSelection
{
    private WhisperLanguageSelection(string? languageTag)
    {
        LanguageTag = languageTag;
    }

    public static WhisperLanguageSelection AutoDetect { get; } = new(null);

    /// <summary>A BCP-47 tag, or <see langword="null"/> when the provider should detect.</summary>
    public string? LanguageTag { get; }

    public bool IsAutoDetect => LanguageTag is null;

    public static WhisperLanguageSelection Explicit(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        string candidate = languageTag.Trim();
        if (candidate.Length > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageTag),
                "Language tags cannot exceed 32 characters.");
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(candidate, predefinedOnly: true);
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException(
                "Language tags must be a recognized BCP-47 culture name.",
                nameof(languageTag),
                exception);
        }

        return new WhisperLanguageSelection(culture.Name);
    }

    public override string ToString() => LanguageTag ?? "auto";
}
