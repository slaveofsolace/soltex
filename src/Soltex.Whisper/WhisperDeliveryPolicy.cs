namespace Soltex.Whisper;

public sealed class WhisperDeliveryPolicy
{
    private readonly bool _allowAutomaticSubmission;

    public WhisperDeliveryPolicy(bool allowAutomaticSubmission = true)
    {
        _allowAutomaticSubmission = allowAutomaticSubmission;
    }

    public WhisperDeliveryDecision Evaluate(
        WhisperPipelineResult pipeline,
        WhisperTargetContext target,
        WhisperAppProfile? profile,
        bool autoSendEnabled,
        bool dedicatedSubmitShortcut = false,
        bool soltexIsElevated = false)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(target);

        bool submitRequested = pipeline.SubmitRequested || dedicatedSubmitShortcut;
        WhisperSubmitOrigin submitOrigin = pipeline.SubmitRequested
            ? pipeline.SubmitOrigin
            : dedicatedSubmitShortcut
                ? WhisperSubmitOrigin.DedicatedShortcut
                : WhisperSubmitOrigin.None;

        if (pipeline.Text.Length == 0 && !submitRequested)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.None,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                RestoreClipboard: false,
                "The transcript is empty.");
        }

        if (target.IsPassword)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.None,
                pipeline.Text,
                submitOrigin,
                RestoreClipboard: false,
                "Whisper does not insert, copy, or submit text into password fields.");
        }

        if (!target.IsKnown || !target.IsEditable || target.IsReadOnly)
        {
            return pipeline.Text.Length == 0
                ? new WhisperDeliveryDecision(
                    WhisperDeliveryKind.None,
                    pipeline.Text,
                    submitOrigin,
                    RestoreClipboard: false,
                    "The focused target is not a confirmed editable text control.")
                : new WhisperDeliveryDecision(
                    WhisperDeliveryKind.CopyText,
                    pipeline.Text,
                    WhisperSubmitOrigin.None,
                    RestoreClipboard: false,
                    "The focused target is not a confirmed editable text control; copy is the safe fallback.");
        }

        if (target.IsElevated && !soltexIsElevated)
        {
            return pipeline.Text.Length == 0
                ? new WhisperDeliveryDecision(
                    WhisperDeliveryKind.None,
                    pipeline.Text,
                    submitOrigin,
                    RestoreClipboard: false,
                    "A non-elevated Soltex process cannot safely submit into an elevated target.")
                : new WhisperDeliveryDecision(
                    WhisperDeliveryKind.CopyText,
                    pipeline.Text,
                    WhisperSubmitOrigin.None,
                    RestoreClipboard: false,
                    "The target is elevated; copy is used instead of synthetic input.");
        }

        bool restoreClipboard = profile?.ClipboardFallbackAllowed == true;
        if (!submitRequested)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.InsertText,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                restoreClipboard,
                "Insert the finalized transcript without submitting it.");
        }

        if (!_allowAutomaticSubmission)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.InsertText,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                restoreClipboard,
                "Automatic submission is disabled by this policy instance.");
        }

        if (!autoSendEnabled)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.InsertText,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                restoreClipboard,
                "Auto-send is disabled globally.");
        }

        if (profile is null ||
            !profile.MatchesProcess(target.ProcessName) ||
            !profile.AutoSendAllowed)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.InsertText,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                restoreClipboard,
                "Auto-send is not allowed for the focused application.");
        }

        if (target.Kind == WhisperTargetKind.Terminal &&
            !profile.TerminalAutoSendAllowed)
        {
            return new WhisperDeliveryDecision(
                WhisperDeliveryKind.InsertText,
                pipeline.Text,
                WhisperSubmitOrigin.None,
                restoreClipboard,
                "Terminal submission requires a separate per-application opt-in.");
        }

        WhisperDeliveryKind deliveryKind = pipeline.Text.Length == 0
            ? WhisperDeliveryKind.SubmitOnly
            : WhisperDeliveryKind.InsertAndSubmit;
        return new WhisperDeliveryDecision(
            deliveryKind,
            pipeline.Text,
            submitOrigin,
            restoreClipboard,
            "Auto-send is allowed by the global and per-application policies.");
    }
}
