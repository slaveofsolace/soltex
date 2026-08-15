namespace Soltex.Whisper;

public enum WhisperSubmitDispatchOutcome
{
    NotRequested,
    Denied,
    Dispatched,
    DispatchRejected
}

/// <summary>
/// Ephemeral target-owned text used only for an immediate comparison. Callers must
/// not retain, serialize, diagnose, or publish <see cref="Text"/>.
/// </summary>
public sealed class WhisperTargetReadback
{
    public WhisperTargetReadback(string text, WhisperVerificationMethod method)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > WhisperLimits.MaximumReadbackCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Target read-back cannot exceed {WhisperLimits.MaximumReadbackCharacters} characters.");
        }

        if (method == WhisperVerificationMethod.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(method),
                "A target read-back must identify the automation method used.");
        }

        Text = text;
        Method = method;
    }

    public string Text { get; }

    public WhisperVerificationMethod Method { get; }
}

public sealed record WhisperVerifiedSubmitRequest(
    WhisperDeliveryDecision Decision,
    WhisperTargetSnapshot CapturedTarget,
    WhisperTextDeliveryResult Delivery,
    bool FirstUseWarningAccepted);

/// <summary>
/// Content-free outcome of the final submission boundary.
/// </summary>
public sealed record WhisperVerifiedSubmitResult(
    WhisperSubmitAuthorization Authorization,
    WhisperInsertionVerification Verification,
    WhisperTargetKind TargetKind,
    WhisperSubmitOrigin Origin,
    WhisperSubmitDispatchOutcome Outcome)
{
    public bool EnterDispatched => Outcome == WhisperSubmitDispatchOutcome.Dispatched;
}
