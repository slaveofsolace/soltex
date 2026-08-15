using System.Collections.ObjectModel;
using System.Globalization;

namespace Soltex.Whisper;

/// <summary>
/// Content-free session outcome categories. Diagnostics record what happened, never
/// what was said.
/// </summary>
public enum WhisperDiagnosticEventKind
{
    SessionStarted,
    SessionCancelled,
    SessionFaulted,
    TranscriptionSucceeded,
    TranscriptionFailed,
    ShortcutsRegistered,
    ShortcutRegistrationFailed,
    TargetInspected,
    TextInserted,
    InsertionFallback,
    SubmitAllowed,
    SubmitDenied,
    CaptureDeviceLost
}

/// <summary>
/// Duration reported as a bucket rather than a precise value. Exact durations of
/// short utterances are themselves a weak signal about content and length, so the
/// evidence trail keeps only the coarse band.
/// </summary>
public enum WhisperDurationBucket
{
    UnderFiveSeconds,
    UnderThirtySeconds,
    UnderTwoMinutes,
    UnderTenMinutes,
    Extended
}

/// <summary>
/// One structured, content-free diagnostic record.
/// </summary>
public sealed class WhisperDiagnosticEvent
{
    public const int MaximumDetailCharacters = 200;

    public WhisperDiagnosticEvent(
        DateTimeOffset observedAtUtc,
        WhisperDiagnosticEventKind kind,
        WhisperCaptureMode? mode,
        WhisperTargetKind targetKind,
        string? detail)
    {
        ObservedAtUtc = observedAtUtc;
        Kind = kind;
        Mode = mode;
        TargetKind = targetKind;
        Detail = detail is null ? null : WhisperRedaction.Sanitize(detail, MaximumDetailCharacters);
    }

    public DateTimeOffset ObservedAtUtc { get; }

    public WhisperDiagnosticEventKind Kind { get; }

    public WhisperCaptureMode? Mode { get; }

    public WhisperTargetKind TargetKind { get; }

    /// <summary>A sanitized policy reason. Never a transcript, path, or provider payload.</summary>
    public string? Detail { get; }

    public string ToEvidenceLine()
    {
        string mode = Mode?.ToString() ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ObservedAtUtc:O} kind={Kind} mode={mode} target={TargetKind} detail={Detail ?? "-"}");
    }
}

/// <summary>
/// Removes anything that could carry dictated content out of Soltex.
/// </summary>
/// <remarks>
/// Provider errors are the main hazard: adapters routinely echo the submitted text
/// or a request URL back inside an exception message. Everything that reaches
/// diagnostics passes through here first, so a careless adapter cannot leak a
/// transcript into a log file or a CI artifact.
/// </remarks>
public static class WhisperRedaction
{
    private const string Placeholder = "[redacted]";

    public static string Sanitize(string detail, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(detail);

        if (maximumCharacters < 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                "Sanitized detail must allow at least 16 characters.");
        }

        // Quoted spans are collapsed whole, before tokenizing. A provider that echoes
        // back "my private sentence" must not survive as three separate words that
        // individually look harmless.
        string withoutQuotedSpans = RedactQuotedSpans(detail);

        List<string> safeWords = [];
        foreach (string word in withoutQuotedSpans.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            safeWords.Add(LooksSensitive(word) ? Placeholder : StripControl(word));
        }

        string joined = string.Join(' ', safeWords);
        return joined.Length <= maximumCharacters
            ? joined
            : joined[..maximumCharacters];
    }

    /// <summary>
    /// Replaces every double-quoted or backticked span with the placeholder. An
    /// unterminated span is redacted to the end of the string rather than left intact.
    /// </summary>
    private static string RedactQuotedSpans(string detail)
    {
        if (detail.AsSpan().IndexOfAny('"', '`') < 0)
        {
            return detail;
        }

        System.Text.StringBuilder builder = new(detail.Length);
        int index = 0;
        while (index < detail.Length)
        {
            char character = detail[index];
            if (character is '"' or '`')
            {
                int closing = detail.IndexOf(character, index + 1);
                builder.Append(' ').Append(Placeholder).Append(' ');
                if (closing < 0)
                {
                    return builder.ToString();
                }

                index = closing + 1;
                continue;
            }

            builder.Append(character);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Redacts tokens that commonly carry user or environment data: URLs, file paths,
    /// email addresses, and quoted fragments echoed back by a provider.
    /// </summary>
    private static bool LooksSensitive(string word)
    {
        if (word.Contains("://", StringComparison.Ordinal) ||
            word.Contains('@', StringComparison.Ordinal) ||
            word.Contains('\\', StringComparison.Ordinal) ||
            word.Contains('"', StringComparison.Ordinal))
        {
            return true;
        }

        // Drive-qualified or absolute paths.
        if (word.Length >= 2 && word[1] == ':')
        {
            return true;
        }

        return word.StartsWith('/') && word.Length > 1;
    }

    private static string StripControl(string word)
    {
        return word.Any(char.IsControl)
            ? new string(word.Where(character => !char.IsControl(character)).ToArray())
            : word;
    }

    public static WhisperDurationBucket ToBucket(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(5))
        {
            return WhisperDurationBucket.UnderFiveSeconds;
        }

        if (duration < TimeSpan.FromSeconds(30))
        {
            return WhisperDurationBucket.UnderThirtySeconds;
        }

        if (duration < TimeSpan.FromMinutes(2))
        {
            return WhisperDurationBucket.UnderTwoMinutes;
        }

        return duration < TimeSpan.FromMinutes(10)
            ? WhisperDurationBucket.UnderTenMinutes
            : WhisperDurationBucket.Extended;
    }
}

/// <summary>
/// Bounded in-memory diagnostic trail. It never grows without limit and never
/// persists on its own.
/// </summary>
public sealed class WhisperDiagnosticLog
{
    public const int MaximumEvents = 250;

    private readonly object _sync = new();
    private readonly Queue<WhisperDiagnosticEvent> _events = [];

    public WhisperDiagnosticLog(int capacity = MaximumEvents)
    {
        if (capacity is < 1 or > MaximumEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                $"The diagnostic log holds between 1 and {MaximumEvents} events.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Record(WhisperDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (_sync)
        {
            _events.Enqueue(diagnosticEvent);
            while (_events.Count > Capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public ReadOnlyCollection<WhisperDiagnosticEvent> CreateSnapshot()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_events.ToArray());
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
        }
    }
}
