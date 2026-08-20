using System.Collections.ObjectModel;

namespace Soltex.Whisper;

/// <summary>
/// The closed set of transforms Command Mode can request. The set is closed on
/// purpose: an unrecognized spoken command resolves to <see cref="Unsupported"/>
/// and is refused, never forwarded somewhere that might act on it.
/// </summary>
public enum WhisperCommandKind
{
    Unsupported,
    Rewrite,
    Shorten,
    Lengthen,
    Summarize,
    MakeFormal,
    MakeCasual,
    FixGrammar,
    ConvertToBullets,
    Translate
}

/// <summary>
/// What Command Mode will act on: the user's current selection, or the caret when
/// nothing is selected.
/// </summary>
public enum WhisperCommandScope
{
    Selection,
    Caret
}

public sealed record WhisperCommandRequest(
    WhisperCommandKind Kind,
    WhisperCommandScope Scope,
    string? TargetLanguageTag,
    bool RequiresPreview,
    string Reason)
{
    public bool IsSupported => Kind != WhisperCommandKind.Unsupported;
}

/// <summary>
/// Turns a spoken Command Mode utterance into a bounded request.
/// </summary>
/// <remarks>
/// Two rules make this safe. First, matching is against a fixed phrase table rather
/// than free interpretation, so a sentence that merely sounds like an instruction
/// cannot become one. Second, nothing here can name an application, a file, a
/// recipient, or a shell command — the result is only ever a text transform.
/// </remarks>
public static class WhisperCommandParser
{
    private static readonly ReadOnlyDictionary<string, WhisperCommandKind> PhraseTable =
        new(new Dictionary<string, WhisperCommandKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["rewrite"] = WhisperCommandKind.Rewrite,
            ["rewrite this"] = WhisperCommandKind.Rewrite,
            ["rephrase"] = WhisperCommandKind.Rewrite,
            ["rephrase this"] = WhisperCommandKind.Rewrite,
            ["shorten"] = WhisperCommandKind.Shorten,
            ["shorten this"] = WhisperCommandKind.Shorten,
            ["make this shorter"] = WhisperCommandKind.Shorten,
            ["make it shorter"] = WhisperCommandKind.Shorten,
            ["lengthen"] = WhisperCommandKind.Lengthen,
            ["expand"] = WhisperCommandKind.Lengthen,
            ["make this longer"] = WhisperCommandKind.Lengthen,
            ["make it longer"] = WhisperCommandKind.Lengthen,
            ["summarize"] = WhisperCommandKind.Summarize,
            ["summarise"] = WhisperCommandKind.Summarize,
            ["summarize this"] = WhisperCommandKind.Summarize,
            ["make this formal"] = WhisperCommandKind.MakeFormal,
            ["make it formal"] = WhisperCommandKind.MakeFormal,
            ["make this more formal"] = WhisperCommandKind.MakeFormal,
            ["make this casual"] = WhisperCommandKind.MakeCasual,
            ["make it casual"] = WhisperCommandKind.MakeCasual,
            ["make this more casual"] = WhisperCommandKind.MakeCasual,
            ["fix grammar"] = WhisperCommandKind.FixGrammar,
            ["fix the grammar"] = WhisperCommandKind.FixGrammar,
            ["fix spelling and grammar"] = WhisperCommandKind.FixGrammar,
            ["convert to bullets"] = WhisperCommandKind.ConvertToBullets,
            ["make this a list"] = WhisperCommandKind.ConvertToBullets,
            ["turn this into bullets"] = WhisperCommandKind.ConvertToBullets
        });

    private const string TranslatePrefix = "translate to ";

    /// <summary>
    /// Transforms that can change meaning rather than only presentation. These always
    /// require a preview before they replace the user's existing text.
    /// </summary>
    private static readonly ReadOnlyCollection<WhisperCommandKind> MeaningChanging =
        Array.AsReadOnly(new[]
        {
            WhisperCommandKind.Rewrite,
            WhisperCommandKind.Shorten,
            WhisperCommandKind.Lengthen,
            WhisperCommandKind.Summarize,
            WhisperCommandKind.MakeFormal,
            WhisperCommandKind.MakeCasual,
            WhisperCommandKind.Translate
        });

    public static WhisperCommandRequest Parse(
        string spokenCommand,
        bool hasSelection)
    {
        ArgumentNullException.ThrowIfNull(spokenCommand);

        string normalized = Normalize(spokenCommand);
        WhisperCommandScope scope = hasSelection
            ? WhisperCommandScope.Selection
            : WhisperCommandScope.Caret;

        if (normalized.Length == 0)
        {
            return Unsupported(scope, "Command Mode heard no command.");
        }

        if (PhraseTable.TryGetValue(normalized, out WhisperCommandKind kind))
        {
            return Build(kind, scope, targetLanguageTag: null);
        }

        if (normalized.StartsWith(TranslatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string languageName = normalized[TranslatePrefix.Length..].Trim();
            if (WhisperTranslationLanguages.TryResolve(languageName, out string? languageTag))
            {
                return Build(WhisperCommandKind.Translate, scope, languageTag);
            }

            return Unsupported(
                scope,
                "Command Mode only translates into a language chosen in Whisper settings.");
        }

        return Unsupported(
            scope,
            "Command Mode did not recognize that instruction and will not guess at it.");
    }

    private static WhisperCommandRequest Build(
        WhisperCommandKind kind,
        WhisperCommandScope scope,
        string? targetLanguageTag)
    {
        // Replacing a selection is destructive, so meaning-changing transforms preview
        // first. Inserting at the caret adds text without destroying any, so it does not.
        bool requiresPreview =
            scope == WhisperCommandScope.Selection &&
            MeaningChanging.Contains(kind);

        string reason = requiresPreview
            ? "This transform can change meaning and replaces the selection, so it previews first."
            : "This transform is applied directly.";

        return new WhisperCommandRequest(kind, scope, targetLanguageTag, requiresPreview, reason);
    }

    private static WhisperCommandRequest Unsupported(WhisperCommandScope scope, string reason) =>
        new(WhisperCommandKind.Unsupported, scope, null, RequiresPreview: false, reason);

    private static string Normalize(string spokenCommand)
    {
        string collapsed = string.Join(
            ' ',
            spokenCommand.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.TrimEnd('.', ',', '!', '?', ';', ':').Trim();
    }
}

/// <summary>
/// The translation languages Command Mode accepts, by spoken name. Keeping this a
/// fixed table means a misheard word becomes a refusal rather than an unexpected
/// language.
/// </summary>
public static class WhisperTranslationLanguages
{
    private static readonly ReadOnlyDictionary<string, string> ByName =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arabic"] = "ar",
            ["chinese"] = "zh",
            ["dutch"] = "nl",
            ["english"] = "en",
            ["french"] = "fr",
            ["german"] = "de",
            ["hindi"] = "hi",
            ["italian"] = "it",
            ["japanese"] = "ja",
            ["korean"] = "ko",
            ["polish"] = "pl",
            ["portuguese"] = "pt",
            ["russian"] = "ru",
            ["spanish"] = "es",
            ["turkish"] = "tr",
            ["ukrainian"] = "uk"
        });

    public static ReadOnlyCollection<string> SupportedNames { get; } =
        Array.AsReadOnly(ByName.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

    public static bool TryResolve(string spokenName, out string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(spokenName))
        {
            languageTag = null;
            return false;
        }

        return ByName.TryGetValue(spokenName.Trim(), out languageTag);
    }
}
