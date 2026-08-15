using System.Text;

namespace Soltex.Whisper;

public sealed record WhisperTerminalCommandResult(
    string Text,
    bool SubmitRequested);

public static class WhisperTerminalSubmitCommand
{
    private const string SubmitPhrase = "press enter";
    private static readonly char[] TrailingCommandPunctuation = ['.', ',', '!', '?', ';', ':'];

    public static WhisperTerminalCommandResult Parse(string transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        string trimmed = transcript.TrimEnd();
        string commandCandidate = trimmed.TrimEnd(TrailingCommandPunctuation).TrimEnd();
        if (!commandCandidate.EndsWith(SubmitPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return new WhisperTerminalCommandResult(transcript, SubmitRequested: false);
        }

        int phraseStart = commandCandidate.Length - SubmitPhrase.Length;
        if (phraseStart > 0 && !char.IsWhiteSpace(commandCandidate[phraseStart - 1]))
        {
            return new WhisperTerminalCommandResult(transcript, SubmitRequested: false);
        }

        string text = commandCandidate[..phraseStart].TrimEnd();
        return new WhisperTerminalCommandResult(text, SubmitRequested: true);
    }
}

public sealed class WhisperTextPipeline
{
    private readonly WhisperTextOptions _defaultOptions;

    public WhisperTextPipeline(WhisperTextOptions? defaultOptions = null)
    {
        _defaultOptions = defaultOptions ?? WhisperTextOptions.Default;
    }

    public WhisperPipelineResult Process(
        string rawTranscript,
        IEnumerable<WhisperSnippet>? snippets = null,
        WhisperTextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rawTranscript);

        if (rawTranscript.Length > WhisperLimits.MaximumTranscriptCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawTranscript),
                $"Raw transcripts cannot exceed {WhisperLimits.MaximumTranscriptCharacters} characters.");
        }

        WhisperTextOptions effectiveOptions = options ?? _defaultOptions;
        List<string> appliedOperations = [];
        string working = rawTranscript.Trim();
        bool submitRequested = false;
        WhisperSubmitOrigin submitOrigin = WhisperSubmitOrigin.None;

        if (effectiveOptions.DetectTerminalSubmit)
        {
            WhisperTerminalCommandResult terminalCommand = WhisperTerminalSubmitCommand.Parse(working);
            working = terminalCommand.Text;
            if (terminalCommand.SubmitRequested)
            {
                submitRequested = true;
                submitOrigin = WhisperSubmitOrigin.TerminalPhrase;
                appliedOperations.Add("terminal-submit");
            }
        }

        bool snippetExpanded = false;
        if (effectiveOptions.ExpandSnippets && snippets is not null)
        {
            WhisperSnippet[] materializedSnippets = snippets.ToArray();
            ValidateSnippets(materializedSnippets);

            WhisperSnippet? match = materializedSnippets.FirstOrDefault(
                snippet => string.Equals(
                    snippet.Cue,
                    working,
                    StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                working = match.Content;
                snippetExpanded = true;
                appliedOperations.Add("snippet-expansion");
            }
        }

        if (!snippetExpanded && effectiveOptions.Backtrack)
        {
            WhisperTransformResult backtracked = WhisperBacktrackProcessor.Apply(working);
            working = backtracked.Text;
            if (backtracked.Changed)
            {
                appliedOperations.Add("backtrack");
            }
        }

        if (!snippetExpanded && effectiveOptions.SmartFormatting)
        {
            WhisperTransformResult formatted = WhisperSmartFormatter.Apply(working);
            working = formatted.Text;
            if (formatted.Changed)
            {
                appliedOperations.Add("smart-formatting");
            }
        }

        return new WhisperPipelineResult(
            working,
            submitRequested,
            submitOrigin,
            appliedOperations);
    }

    private static void ValidateSnippets(WhisperSnippet[] snippets)
    {
        if (snippets.Length > WhisperLimits.MaximumSnippetCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snippets),
                $"No more than {WhisperLimits.MaximumSnippetCount} snippets can be evaluated per transcript.");
        }

        IGrouping<string, WhisperSnippet>? duplicate = snippets
            .GroupBy(snippet => snippet.Cue, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Snippet cue '{duplicate.Key}' is defined more than once.",
                nameof(snippets));
        }
    }
}

internal sealed record WhisperTransformResult(
    string Text,
    bool Changed);

internal static class WhisperBacktrackProcessor
{
    private static readonly char[] TokenPunctuation = ['.', ',', '!', '?', ';', ':'];

    internal static WhisperTransformResult Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new WhisperTransformResult(text.Trim(), Changed: false);
        }

        string[] tokens = text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<string> output = [];
        bool changed = false;

        for (int index = 0; index < tokens.Length; index++)
        {
            if (index + 1 < tokens.Length &&
                IsCorrectionPhrase(tokens[index], tokens[index + 1]))
            {
                if (output.Count > 0)
                {
                    output.RemoveAt(output.Count - 1);
                }

                index++;
                changed = true;
                continue;
            }

            output.Add(tokens[index]);
        }

        return new WhisperTransformResult(string.Join(' ', output), changed);
    }

    private static bool IsCorrectionPhrase(string first, string second)
    {
        string normalizedFirst = first.Trim(TokenPunctuation);
        string normalizedSecond = second.Trim(TokenPunctuation);
        return
            (string.Equals(normalizedFirst, "scratch", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(normalizedFirst, "delete", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(normalizedSecond, "that", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class WhisperSmartFormatter
{
    private const string LineBreakToken = "\n";

    internal static WhisperTransformResult Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new WhisperTransformResult(text.Trim(), Changed: false);
        }

        List<string> tokens = Tokenize(text);
        StringBuilder builder = new(text.Length);
        bool changed = false;

        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (token == LineBreakToken)
            {
                AppendLineBreaks(builder, 1);
                continue;
            }

            if (Matches(tokens, index, "new", "paragraph"))
            {
                AppendLineBreaks(builder, 2);
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "new", "line"))
            {
                AppendLineBreaks(builder, 1);
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "question", "mark"))
            {
                AppendPunctuation(builder, '?');
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "full", "stop"))
            {
                AppendPunctuation(builder, '.');
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "exclamation", "mark") ||
                Matches(tokens, index, "exclamation", "point"))
            {
                AppendPunctuation(builder, '!');
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "open", "parenthesis"))
            {
                AppendOpeningDelimiter(builder, '(');
                index++;
                changed = true;
                continue;
            }

            if (Matches(tokens, index, "close", "parenthesis"))
            {
                AppendPunctuation(builder, ')');
                index++;
                changed = true;
                continue;
            }

            if (TryMapSingleToken(token, out char punctuation))
            {
                AppendPunctuation(builder, punctuation);
                changed = true;
                continue;
            }

            if (string.Equals(token, "bullet", StringComparison.OrdinalIgnoreCase))
            {
                AppendLineBreaks(builder, 1);
                builder.Append("• ");
                changed = true;
                continue;
            }

            AppendWord(builder, token);
        }

        return new WhisperTransformResult(builder.ToString().Trim(), changed);
    }

    private static List<string> Tokenize(string text)
    {
        List<string> tokens = [];
        StringBuilder token = new();

        void FlushToken()
        {
            if (token.Length == 0)
            {
                return;
            }

            tokens.Add(token.ToString());
            token.Clear();
        }

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\r')
            {
                FlushToken();
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                tokens.Add(LineBreakToken);
                continue;
            }

            if (character == '\n')
            {
                FlushToken();
                tokens.Add(LineBreakToken);
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                FlushToken();
                continue;
            }

            token.Append(character);
        }

        FlushToken();
        return tokens;
    }

    private static bool Matches(
        List<string> tokens,
        int index,
        string first,
        string second)
    {
        return index + 1 < tokens.Count &&
            string.Equals(tokens[index], first, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(tokens[index + 1], second, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMapSingleToken(string token, out char punctuation)
    {
        if (string.Equals(token, "comma", StringComparison.OrdinalIgnoreCase))
        {
            punctuation = ',';
            return true;
        }

        if (string.Equals(token, "period", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "full-stop", StringComparison.OrdinalIgnoreCase))
        {
            punctuation = '.';
            return true;
        }

        if (string.Equals(token, "colon", StringComparison.OrdinalIgnoreCase))
        {
            punctuation = ':';
            return true;
        }

        if (string.Equals(token, "semicolon", StringComparison.OrdinalIgnoreCase))
        {
            punctuation = ';';
            return true;
        }

        punctuation = default;
        return false;
    }

    private static void AppendWord(StringBuilder builder, string word)
    {
        if (builder.Length > 0 &&
            !char.IsWhiteSpace(builder[^1]) &&
            builder[^1] is not '(' and not '[' and not '{')
        {
            builder.Append(' ');
        }

        builder.Append(word);
    }

    private static void AppendOpeningDelimiter(StringBuilder builder, char delimiter)
    {
        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
        {
            builder.Append(' ');
        }

        builder.Append(delimiter);
    }

    private static void AppendPunctuation(StringBuilder builder, char punctuation)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        builder.Append(punctuation);
    }

    private static void AppendLineBreaks(StringBuilder builder, int count)
    {
        while (builder.Length > 0 &&
               (builder[^1] == ' ' || builder[^1] == '\t'))
        {
            builder.Length--;
        }

        int existingLineBreaks = 0;
        for (int index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
        {
            existingLineBreaks++;
        }

        for (int index = existingLineBreaks; index < count; index++)
        {
            builder.Append('\n');
        }
    }
}
