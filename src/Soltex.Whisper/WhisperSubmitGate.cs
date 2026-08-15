using System.Text;

namespace Soltex.Whisper;

/// <summary>
/// How Whisper put text into the target. Recorded as evidence; never inferred from
/// the return value of a synthetic-input call.
/// </summary>
public enum WhisperInsertionMethod
{
    None,
    AutomationValue,
    AutomationTextRange,
    ClipboardPaste
}

/// <summary>
/// How the inserted text was read back. <see cref="None"/> means insertion was not
/// proven and therefore submission cannot be authorized.
/// </summary>
public enum WhisperVerificationMethod
{
    None,
    AutomationValueRead,
    AutomationTextRead,
    AdapterConfirmation
}

public sealed record WhisperInsertionVerification(
    bool Verified,
    WhisperVerificationMethod Method,
    string Reason)
{
    public static WhisperInsertionVerification Unavailable { get; } = new(
        Verified: false,
        WhisperVerificationMethod.None,
        "The target exposes no readable state, so insertion could not be verified.");
}

/// <summary>
/// Compares what Whisper meant to insert with what the target actually reports.
/// Only documented, lossless normalizations are tolerated; anything else is a
/// verification failure rather than a near-enough match.
/// </summary>
public static class WhisperInsertionVerifier
{
    public static WhisperInsertionVerification Verify(
        string expectedText,
        string? observedText,
        WhisperVerificationMethod method,
        WhisperTargetKind targetKind)
    {
        ArgumentNullException.ThrowIfNull(expectedText);

        if (method == WhisperVerificationMethod.None)
        {
            return WhisperInsertionVerification.Unavailable;
        }

        if (observedText is null)
        {
            return new WhisperInsertionVerification(
                Verified: false,
                WhisperVerificationMethod.None,
                "The target returned no readable text after insertion.");
        }

        string expected = Normalize(expectedText, targetKind);
        string observed = Normalize(observedText, targetKind);

        if (string.Equals(expected, observed, StringComparison.Ordinal))
        {
            return new WhisperInsertionVerification(
                Verified: true,
                method,
                "The target reports exactly the finalized transcript.");
        }

        // A read that returns the whole field must still end with what Whisper added,
        // otherwise the insertion landed somewhere else or was altered in transit.
        if (expected.Length > 0 && observed.EndsWith(expected, StringComparison.Ordinal))
        {
            return new WhisperInsertionVerification(
                Verified: true,
                method,
                "The target reports existing content followed by the finalized transcript.");
        }

        return new WhisperInsertionVerification(
            Verified: false,
            WhisperVerificationMethod.None,
            "The text read back from the target does not match the finalized transcript.");
    }

    /// <summary>
    /// Documented normalization: line endings collapse to LF, no-break and zero-width
    /// characters introduced by rich-text hosts collapse to their plain equivalents,
    /// and trailing whitespace is ignored. Interior spacing and casing are preserved.
    /// </summary>
    internal static string Normalize(string text, WhisperTargetKind targetKind)
    {
        const char NoBreakSpace = '\u00A0';
        const char NarrowNoBreakSpace = '\u202F';
        const char ZeroWidthSpace = '\u200B';
        const char ByteOrderMark = '\uFEFF';

        StringBuilder builder = new(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            switch (character)
            {
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    builder.Append('\n');
                    break;
                case NoBreakSpace:
                case NarrowNoBreakSpace:
                    builder.Append(' ');
                    break;
                case ZeroWidthSpace:
                case ByteOrderMark:
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        // Terminals echo a trailing newline of their own; plain and rich text do not.
        string normalized = builder.ToString();
        return targetKind == WhisperTargetKind.Terminal
            ? normalized.TrimEnd('\n', ' ', '\t')
            : normalized.TrimEnd();
    }
}

public sealed record WhisperSubmitAuthorization(
    bool Allowed,
    string Reason,
    WhisperVerificationMethod Verification,
    WhisperTargetDrift Drift);

/// <summary>
/// The last gate before Enter is emitted. <see cref="WhisperDeliveryPolicy"/> decides
/// whether submission is <em>permitted</em>; this gate decides whether it is
/// <em>still true right now</em>, after insertion, against the same control.
/// Every branch fails closed.
/// </summary>
public static class WhisperSubmitGate
{
    public static WhisperSubmitAuthorization Evaluate(
        WhisperDeliveryDecision decision,
        WhisperTargetSnapshot capturedTarget,
        WhisperTargetSnapshot currentTarget,
        WhisperInsertionVerification verification,
        bool firstUseWarningAccepted,
        bool cancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(capturedTarget);
        ArgumentNullException.ThrowIfNull(currentTarget);
        ArgumentNullException.ThrowIfNull(verification);

        if (decision.Kind is not (WhisperDeliveryKind.InsertAndSubmit or WhisperDeliveryKind.SubmitOnly))
        {
            return Deny(
                "The delivery decision does not request submission.",
                WhisperTargetDrift.None);
        }

        if (decision.SubmitOrigin == WhisperSubmitOrigin.None)
        {
            return Deny(
                "A submission must identify whether it came from the spoken phrase or the submit shortcut.",
                WhisperTargetDrift.None);
        }

        if (cancellationRequested)
        {
            return Deny(
                "Submission was cancelled before the irreversible step began.",
                WhisperTargetDrift.None);
        }

        if (!firstUseWarningAccepted)
        {
            return Deny(
                "The first-use auto-send warning has not been accepted.",
                WhisperTargetDrift.None);
        }

        WhisperTargetDrift drift = capturedTarget.CompareWith(currentTarget);
        if (drift != WhisperTargetDrift.None)
        {
            return Deny(
                $"The focused target changed before submission ({drift}).",
                drift);
        }

        // SubmitOnly carries no text, so there is nothing to verify; every other
        // submission must prove its text actually landed.
        if (decision.Kind == WhisperDeliveryKind.InsertAndSubmit && !verification.Verified)
        {
            return Deny(
                verification.Reason,
                WhisperTargetDrift.None);
        }

        WhisperVerificationMethod method = decision.Kind == WhisperDeliveryKind.SubmitOnly
            ? WhisperVerificationMethod.None
            : verification.Method;

        return new WhisperSubmitAuthorization(
            Allowed: true,
            "Insertion is verified and the target still matches the captured target.",
            method,
            WhisperTargetDrift.None);
    }

    private static WhisperSubmitAuthorization Deny(string reason, WhisperTargetDrift drift) =>
        new(Allowed: false, reason, WhisperVerificationMethod.None, drift);
}
