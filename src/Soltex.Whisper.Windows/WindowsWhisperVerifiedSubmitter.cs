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
                .TryEmitEnterAsync(
                    authorization,
                    submitTarget ?? throw new InvalidOperationException(
                        "An allowed submission requires a current target."),
                    cancellationToken)
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
        WhisperTargetSnapshot target,
        CancellationToken cancellationToken);
}

internal sealed class WindowsWhisperSubmitPlatform : IWindowsWhisperSubmitPlatform
{
    private const ushort EnterKey = 0x0D;
    private readonly Func<int, bool> _foregroundBelongsToProcess;
    private readonly Func<bool> _modifierIsDown;
    private readonly Func<PasteNative.Input[], uint> _sendInput;

    internal WindowsWhisperSubmitPlatform()
        : this(
            ForegroundBelongsToProcess,
            PasteNative.ModifierIsDown,
            SendInputs)
    {
    }

    internal WindowsWhisperSubmitPlatform(
        Func<int, bool> foregroundBelongsToProcess,
        Func<bool> modifierIsDown,
        Func<PasteNative.Input[], uint> sendInput)
    {
        ArgumentNullException.ThrowIfNull(foregroundBelongsToProcess);
        ArgumentNullException.ThrowIfNull(modifierIsDown);
        ArgumentNullException.ThrowIfNull(sendInput);
        _foregroundBelongsToProcess = foregroundBelongsToProcess;
        _modifierIsDown = modifierIsDown;
        _sendInput = sendInput;
    }

    public ValueTask<bool> TryEmitEnterAsync(
        WhisperSubmitAuthorization authorization,
        WhisperTargetSnapshot target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.TryConsume() ||
            !_foregroundBelongsToProcess(target.Identity.ProcessId) ||
            _modifierIsDown())
        {
            return ValueTask.FromResult(false);
        }

        PasteNative.Input[] inputs = CreateEnterInputs();
        uint sent = _sendInput(inputs);
        if (sent != inputs.Length)
        {
            PasteNative.Input[] cleanup =
            [
                PasteNative.Input.Keyboard(EnterKey, keyUp: true, extraInfo: 0)
            ];
            _ = _sendInput(cleanup);
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    internal static PasteNative.Input[] CreateEnterInputs() =>
        [
            PasteNative.Input.Keyboard(EnterKey, keyUp: false, extraInfo: 0),
            PasteNative.Input.Keyboard(EnterKey, keyUp: true, extraInfo: 0)
        ];

    private static uint SendInputs(PasteNative.Input[] inputs) =>
        PasteNative.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<PasteNative.Input>());

    private static bool ForegroundBelongsToProcess(int expectedProcessId)
    {
        nint foreground = GetForegroundWindow();
        return foreground != 0 &&
            GetWindowThreadProcessId(foreground, out uint processId) != 0 &&
            processId == checked((uint)expectedProcessId);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
