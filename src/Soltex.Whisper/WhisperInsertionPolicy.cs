namespace Soltex.Whisper;

public enum WhisperInsertionAction
{
    None,
    Insert,
    Copy
}

public enum WhisperInsertionFallbackReason
{
    None,
    PolicyRequiredCopy,
    TargetUnknown,
    TargetChanged,
    TargetNotEditable,
    ProtectedField,
    IntegrityBoundary,
    DirectInsertionUnavailable,
    ClipboardUnavailable,
    PasteRejected
}

public enum WhisperClipboardRestoreOutcome
{
    NotRequested,
    Restored,
    SkippedOwnershipChanged,
    Unavailable
}

public sealed record WhisperTextDeliveryRequest(
    WhisperDeliveryDecision Decision,
    WhisperTargetSnapshot? CapturedTarget,
    bool SoltexIsElevated = false);

public sealed record WhisperInsertionAuthorization(
    WhisperInsertionAction Action,
    WhisperInsertionFallbackReason FallbackReason,
    string Reason);

public sealed record WhisperTextDeliveryResult(
    WhisperInsertionMethod Method,
    WhisperInsertionFallbackReason FallbackReason,
    WhisperClipboardRestoreOutcome ClipboardRestore,
    bool MutationDispatched,
    bool Copied);

/// <summary>
/// Re-evaluates a delivery decision against the focused control immediately before
/// any Windows adapter mutates the clipboard or target. All copy-versus-insert
/// decisions stay in the provider-neutral core.
/// </summary>
public static class WhisperInsertionPolicy
{
    public static WhisperInsertionAuthorization Evaluate(
        WhisperTextDeliveryRequest request,
        WhisperTargetSnapshot? currentTarget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Decision);

        WhisperDeliveryDecision decision = request.Decision;
        if (decision.Kind == WhisperDeliveryKind.None ||
            decision.Kind == WhisperDeliveryKind.SubmitOnly ||
            decision.Text.Length == 0)
        {
            return None("This delivery decision has no text insertion step.");
        }

        if (decision.Kind == WhisperDeliveryKind.CopyText)
        {
            return Copy(
                WhisperInsertionFallbackReason.PolicyRequiredCopy,
                "The delivery policy selected the clipboard fallback.");
        }

        if (request.CapturedTarget is null || currentTarget is null)
        {
            return Copy(
                WhisperInsertionFallbackReason.TargetUnknown,
                "The focused target could not be established safely.");
        }

        WhisperTargetDrift drift = request.CapturedTarget.CompareWith(currentTarget);
        if (drift != WhisperTargetDrift.None)
        {
            return drift switch
            {
                WhisperTargetDrift.ProtectedFieldAppeared => Copy(
                    WhisperInsertionFallbackReason.ProtectedField,
                    "The focused control became protected."),
                WhisperTargetDrift.EditabilityLost => Copy(
                    WhisperInsertionFallbackReason.TargetNotEditable,
                    "The focused control is no longer editable."),
                WhisperTargetDrift.ElevationChanged => Copy(
                    WhisperInsertionFallbackReason.IntegrityBoundary,
                    "The target integrity level changed."),
                WhisperTargetDrift.TargetUnknown => Copy(
                    WhisperInsertionFallbackReason.TargetUnknown,
                    "The focused target became unavailable."),
                _ => Copy(
                    WhisperInsertionFallbackReason.TargetChanged,
                    "Focus moved to a different target.")
            };
        }

        WhisperTargetContext context = currentTarget.Context;
        if (context.IsPassword)
        {
            return Copy(
                WhisperInsertionFallbackReason.ProtectedField,
                "The focused control is protected.");
        }

        if (!context.IsKnown || !context.IsEditable || context.IsReadOnly)
        {
            return Copy(
                WhisperInsertionFallbackReason.TargetNotEditable,
                "The focused control is not a confirmed editable target.");
        }

        if (context.IsElevated && !request.SoltexIsElevated)
        {
            return Copy(
                WhisperInsertionFallbackReason.IntegrityBoundary,
                "The target is across an integrity boundary.");
        }

        return new WhisperInsertionAuthorization(
            WhisperInsertionAction.Insert,
            WhisperInsertionFallbackReason.None,
            "The captured target is still the focused editable control.");
    }

    private static WhisperInsertionAuthorization None(string reason) => new(
        WhisperInsertionAction.None,
        WhisperInsertionFallbackReason.None,
        reason);

    private static WhisperInsertionAuthorization Copy(
        WhisperInsertionFallbackReason fallbackReason,
        string reason) => new(
            WhisperInsertionAction.Copy,
            fallbackReason,
            reason);
}
