using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public sealed class WindowsWhisperTextDelivery : IWhisperTextDelivery
{
    // SendInput returning means that Windows accepted the input events; it does not
    // mean the target has already consumed the clipboard. Keep the lease alive for
    // a short, bounded window before an ownership-checked restore.
    public static TimeSpan ClipboardSettleDelay { get; } = TimeSpan.FromMilliseconds(250);

    private readonly IWhisperTargetInspector _targetInspector;
    private readonly IWindowsWhisperInsertionPlatform _platform;

    public WindowsWhisperTextDelivery(IWhisperTargetInspector targetInspector)
        : this(targetInspector, new WindowsWhisperInsertionPlatform())
    {
    }

    internal WindowsWhisperTextDelivery(
        IWhisperTargetInspector targetInspector,
        IWindowsWhisperInsertionPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(targetInspector);
        ArgumentNullException.ThrowIfNull(platform);
        _targetInspector = targetInspector;
        _platform = platform;
    }

    public async ValueTask<WhisperTextDeliveryResult> DeliverAsync(
        WhisperTextDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Decision);
        cancellationToken.ThrowIfCancellationRequested();

        WhisperTargetSnapshot? currentTarget = await _targetInspector
            .InspectAsync(cancellationToken)
            .ConfigureAwait(false);
        WhisperInsertionAuthorization authorization = WhisperInsertionPolicy.Evaluate(
            request,
            currentTarget);

        if (authorization.Action == WhisperInsertionAction.None)
        {
            return NoAction(authorization.FallbackReason);
        }

        if (authorization.Action == WhisperInsertionAction.Copy)
        {
            return await CopyAsync(
                request.Decision.Text,
                authorization.FallbackReason,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.CapturedTarget is null)
        {
            return await CopyAsync(
                request.Decision.Text,
                WhisperInsertionFallbackReason.TargetUnknown,
                cancellationToken).ConfigureAwait(false);
        }

        if (await _platform.TryInsertDirectAsync(
            request.CapturedTarget,
            request.Decision.Text,
            cancellationToken).ConfigureAwait(false))
        {
            return new WhisperTextDeliveryResult(
                WhisperInsertionMethod.AutomationValue,
                WhisperInsertionFallbackReason.None,
                WhisperClipboardRestoreOutcome.NotRequested,
                MutationDispatched: true,
                Copied: false);
        }

        IWindowsWhisperClipboardLease? clipboard = null;
        try
        {
            clipboard = await _platform.StageClipboardAsync(
                request.Decision.Text,
                request.Decision.RestoreClipboard,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WhisperClipboardUnavailableException)
        {
            return NoAction(WhisperInsertionFallbackReason.ClipboardUnavailable);
        }

        await using (clipboard)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WhisperTargetSnapshot? targetBeforePaste = await _targetInspector
                    .InspectAsync(cancellationToken)
                    .ConfigureAwait(false);
                WhisperInsertionAuthorization pasteAuthorization = WhisperInsertionPolicy.Evaluate(
                    request,
                    targetBeforePaste);
                if (pasteAuthorization.Action != WhisperInsertionAction.Insert)
                {
                    return new WhisperTextDeliveryResult(
                        WhisperInsertionMethod.ClipboardCopy,
                        pasteAuthorization.FallbackReason,
                        WhisperClipboardRestoreOutcome.NotRequested,
                        MutationDispatched: false,
                        Copied: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!await _platform.PasteAsync(cancellationToken).ConfigureAwait(false))
                {
                    return new WhisperTextDeliveryResult(
                        WhisperInsertionMethod.ClipboardCopy,
                        WhisperInsertionFallbackReason.PasteRejected,
                        WhisperClipboardRestoreOutcome.NotRequested,
                        MutationDispatched: false,
                        Copied: true);
                }

                WhisperClipboardRestoreOutcome restore =
                    WhisperClipboardRestoreOutcome.NotRequested;
                if (request.Decision.RestoreClipboard)
                {
                    await Task.Delay(ClipboardSettleDelay, CancellationToken.None)
                        .ConfigureAwait(false);
                    restore = await clipboard.TryRestoreAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }

                return new WhisperTextDeliveryResult(
                    WhisperInsertionMethod.ClipboardPaste,
                    WhisperInsertionFallbackReason.DirectInsertionUnavailable,
                    restore,
                    MutationDispatched: true,
                    Copied: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (request.Decision.RestoreClipboard)
                {
                    _ = await clipboard.TryRestoreAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }

                throw;
            }
        }
    }

    private async ValueTask<WhisperTextDeliveryResult> CopyAsync(
        string text,
        WhisperInsertionFallbackReason fallbackReason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _platform.CopyAsync(text, cancellationToken).ConfigureAwait(false);
            return new WhisperTextDeliveryResult(
                WhisperInsertionMethod.ClipboardCopy,
                fallbackReason,
                WhisperClipboardRestoreOutcome.NotRequested,
                MutationDispatched: false,
                Copied: true);
        }
        catch (WhisperClipboardUnavailableException)
        {
            return NoAction(WhisperInsertionFallbackReason.ClipboardUnavailable);
        }
    }

    private static WhisperTextDeliveryResult NoAction(
        WhisperInsertionFallbackReason fallbackReason) => new(
        WhisperInsertionMethod.None,
        fallbackReason,
        WhisperClipboardRestoreOutcome.NotRequested,
        MutationDispatched: false,
        Copied: false);
}

internal interface IWindowsWhisperInsertionPlatform
{
    ValueTask<bool> TryInsertDirectAsync(
        WhisperTargetSnapshot capturedTarget,
        string text,
        CancellationToken cancellationToken);

    ValueTask<IWindowsWhisperClipboardLease> StageClipboardAsync(
        string text,
        bool capturePrevious,
        CancellationToken cancellationToken);

    ValueTask CopyAsync(string text, CancellationToken cancellationToken);

    ValueTask<bool> PasteAsync(CancellationToken cancellationToken);
}

internal interface IWindowsWhisperClipboardLease : IAsyncDisposable
{
    ValueTask<WhisperClipboardRestoreOutcome> TryRestoreAsync(
        CancellationToken cancellationToken);
}

public sealed class WhisperClipboardUnavailableException : Exception
{
    public WhisperClipboardUnavailableException()
        : base("The Windows clipboard is temporarily unavailable.")
    {
    }

    public WhisperClipboardUnavailableException(Exception innerException)
        : base("The Windows clipboard is temporarily unavailable.", innerException)
    {
    }
}
