using System.Runtime.InteropServices;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Performs the final read-back, target reinspection, core authorization, and one
/// irreversible Enter dispatch. It never treats accepted input as proof of receipt.
/// </summary>
public sealed class WindowsWhisperVerifiedSubmitter : IWhisperVerifiedSubmitter
{
    public static TimeSpan VerificationSettleDelay { get; } =
        TimeSpan.FromMilliseconds(75);

    private readonly IWhisperTargetInspector _targetInspector;
    private readonly IWhisperTargetTextReader _textReader;
    private readonly IWindowsWhisperSubmitPlatform _submitPlatform;

    public WindowsWhisperVerifiedSubmitter(
        IWhisperTargetInspector targetInspector)
        : this(
            targetInspector,
            new WindowsWhisperTargetTextReader(),
            new WindowsWhisperSubmitPlatform())
    {
    }

    internal WindowsWhisperVerifiedSubmitter(
        IWhisperTargetInspector targetInspector,
        IWhisperTargetTextReader textReader,
        IWindowsWhisperSubmitPlatform submitPlatform)
    {
        ArgumentNullException.ThrowIfNull(targetInspector);
        ArgumentNullException.ThrowIfNull(textReader);
        ArgumentNullException.ThrowIfNull(submitPlatform);
        _targetInspector = targetInspector;
        _textReader = textReader;
        _submitPlatform = submitPlatform;
    }

    public async ValueTask<WhisperVerifiedSubmitResult> SubmitAsync(
        WhisperVerifiedSubmitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Decision);
        ArgumentNullException.ThrowIfNull(request.CapturedTarget);
        ArgumentNullException.ThrowIfNull(request.Delivery);

        WhisperInsertionVerification verification =
            WhisperInsertionVerification.Unavailable;
        WhisperTargetSnapshot? verificationTarget = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Decision.Kind is not (
                    WhisperDeliveryKind.InsertAndSubmit or
                    WhisperDeliveryKind.SubmitOnly) ||
                request.Decision.SubmitOrigin == WhisperSubmitOrigin.None ||
                !request.FirstUseWarningAccepted)
            {
                WhisperSubmitAuthorization prerequisite =
                    WhisperSubmitGate.Evaluate(
                        request.Decision,
                        request.CapturedTarget,
                        request.CapturedTarget,
                        verification,
                        request.FirstUseWarningAccepted,
                        cancellationRequested: false);
                return Result(
                    request,
                    prerequisite,
                    verification,
                    request.CapturedTarget,
                    request.Decision.Kind is
                        WhisperDeliveryKind.InsertAndSubmit or
                        WhisperDeliveryKind.SubmitOnly
                        ? WhisperSubmitDispatchOutcome.Denied
                        : WhisperSubmitDispatchOutcome.NotRequested);
            }

            if (RequiresInsertionVerification(request))
            {
                await Task.Delay(VerificationSettleDelay, cancellationToken)
                    .ConfigureAwait(false);
                verificationTarget = await _targetInspector
                    .InspectAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (verificationTarget is not null &&
                    request.CapturedTarget.CompareWith(verificationTarget) ==
                    WhisperTargetDrift.None)
                {
                    WhisperTargetReadback? readback = await _textReader
                        .ReadAsync(verificationTarget, cancellationToken)
                        .ConfigureAwait(false);
                    if (readback is not null)
                    {
                        verification = WhisperInsertionVerifier.Verify(
                            request.Decision.Text,
                            readback.Text,
                            readback.Method,
                            verificationTarget.Context.Kind);
                    }
                }
            }

            WhisperTargetSnapshot? submitTarget = await _targetInspector
                .InspectAsync(cancellationToken)
                .ConfigureAwait(false);
            WhisperSubmitAuthorization authorization = WhisperSubmitGate.Evaluate(
                request.Decision,
                request.CapturedTarget,
                submitTarget,
                verification,
                request.FirstUseWarningAccepted,
                cancellationRequested: false);
            if (!authorization.Allowed)
            {
                return Result(
                    request,
                    authorization,
                    verification,
                    submitTarget,
                    WhisperSubmitDispatchOutcome.Denied);
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool dispatched = await _submitPlatform
                .TryEmitEnterAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            return Result(
                request,
                authorization,
                verification,
                submitTarget,
                dispatched
                    ? WhisperSubmitDispatchOutcome.Dispatched
                    : WhisperSubmitDispatchOutcome.DispatchRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WhisperSubmitAuthorization denied = WhisperSubmitGate.Evaluate(
                request.Decision,
                request.CapturedTarget,
                verificationTarget,
                verification,
                request.FirstUseWarningAccepted,
                cancellationRequested: true);
            return Result(
                request,
                denied,
                verification,
                verificationTarget,
                WhisperSubmitDispatchOutcome.Denied);
        }
    }

    private static bool RequiresInsertionVerification(
        WhisperVerifiedSubmitRequest request) =>
        request.Decision.Kind == WhisperDeliveryKind.InsertAndSubmit &&
        request.Delivery.MutationDispatched &&
        request.Delivery.Method is
            WhisperInsertionMethod.AutomationValue or
            WhisperInsertionMethod.AutomationTextRange or
            WhisperInsertionMethod.ClipboardPaste;

    private static WhisperVerifiedSubmitResult Result(
        WhisperVerifiedSubmitRequest request,
        WhisperSubmitAuthorization authorization,
        WhisperInsertionVerification verification,
        WhisperTargetSnapshot? currentTarget,
        WhisperSubmitDispatchOutcome outcome) => new(
            authorization,
            verification,
            currentTarget?.Context.Kind ?? WhisperTargetKind.Unknown,
            request.Decision.SubmitOrigin,
            outcome);
}

internal interface IWindowsWhisperSubmitPlatform
{
    ValueTask<bool> TryEmitEnterAsync(
        WhisperSubmitAuthorization authorization,
        CancellationToken cancellationToken);
}

internal sealed class WindowsWhisperSubmitPlatform : IWindowsWhisperSubmitPlatform
{
    private const ushort EnterKey = 0x0D;

    public ValueTask<bool> TryEmitEnterAsync(
        WhisperSubmitAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.TryConsume() || PasteNative.ModifierIsDown())
        {
            return ValueTask.FromResult(false);
        }

        PasteNative.Input[] inputs =
        [
            PasteNative.Input.Keyboard(EnterKey, keyUp: false),
            PasteNative.Input.Keyboard(EnterKey, keyUp: true)
        ];
        uint sent = PasteNative.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<PasteNative.Input>());
        if (sent != inputs.Length)
        {
            PasteNative.Input[] cleanup =
            [
                PasteNative.Input.Keyboard(EnterKey, keyUp: true)
            ];
            _ = PasteNative.SendInput(
                1,
                cleanup,
                Marshal.SizeOf<PasteNative.Input>());
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }
}
