namespace Soltex.Whisper;

public sealed record WhisperFinalizationRequest(
    string RawTranscript,
    WhisperTargetContext Target,
    WhisperAppProfile? Profile,
    IReadOnlyCollection<WhisperSnippet> Snippets,
    WhisperTextOptions Options,
    bool AutoSendEnabled,
    bool DedicatedSubmitShortcut,
    bool SoltexIsElevated);

public sealed record WhisperFinalizationResult(
    WhisperPipelineResult Pipeline,
    WhisperDeliveryDecision Delivery);

public sealed class WhisperCoordinator
{
    private readonly WhisperTextPipeline _pipeline;
    private readonly WhisperDeliveryPolicy _deliveryPolicy;
    private readonly BoundedWhisperHistory? _history;

    public WhisperCoordinator(
        WhisperTextPipeline? pipeline = null,
        WhisperDeliveryPolicy? deliveryPolicy = null,
        BoundedWhisperHistory? history = null)
    {
        _pipeline = pipeline ?? new WhisperTextPipeline();
        _deliveryPolicy = deliveryPolicy ?? new WhisperDeliveryPolicy();
        _history = history;
    }

    public WhisperFinalizationResult Finalize(
        WhisperFinalizationRequest request,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.Snippets);
        ArgumentNullException.ThrowIfNull(request.Options);

        WhisperPipelineResult pipeline = _pipeline.Process(
            request.RawTranscript,
            request.Snippets,
            request.Options);
        WhisperDeliveryDecision delivery = _deliveryPolicy.Evaluate(
            pipeline,
            request.Target,
            request.Profile,
            request.AutoSendEnabled,
            request.DedicatedSubmitShortcut,
            request.SoltexIsElevated);

        if (_history is not null &&
            delivery.Kind != WhisperDeliveryKind.None &&
            delivery.Text.Length > 0)
        {
            _history.Add(new WhisperHistoryEntry(
                completedAtUtc,
                request.Target.ProcessName,
                delivery.Kind,
                delivery.Text));
        }

        return new WhisperFinalizationResult(pipeline, delivery);
    }
}
