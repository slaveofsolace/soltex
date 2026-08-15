using System.Windows.Automation;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Reads a bounded value from the currently focused control only for immediate
/// insertion verification. The returned text is never logged or persisted here.
/// </summary>
public sealed class WindowsWhisperTargetTextReader : IWhisperTargetTextReader
{
    private readonly IWindowsWhisperTargetTextBackend _backend;
    private readonly WindowsBoundedMtaOperationHost _operations;

    public WindowsWhisperTargetTextReader()
        : this(
            new WindowsUiAutomationTargetTextBackend(),
            WindowsWhisperTargetInspector.DefaultInspectionTimeout)
    {
    }

    internal WindowsWhisperTargetTextReader(
        IWindowsWhisperTargetTextBackend backend,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _operations = new WindowsBoundedMtaOperationHost(timeout);
    }

    public async ValueTask<WhisperTargetReadback?> ReadAsync(
        WhisperTargetSnapshot expectedTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedTarget);
        WindowsBoundedOperationResult<WindowsWhisperTargetReadObservation?> operation =
            await _operations.RunAsync(
                () => _backend.ReadFocused(expectedTarget),
                cancellationToken).ConfigureAwait(false);
        if (!operation.Succeeded ||
            operation.Value is null ||
            operation.Value.Text.Length > WhisperLimits.MaximumReadbackCharacters)
        {
            return null;
        }

        return new WhisperTargetReadback(
            operation.Value.Text,
            operation.Value.Method);
    }
}

internal interface IWindowsWhisperTargetTextBackend
{
    WindowsWhisperTargetReadObservation? ReadFocused(
        WhisperTargetSnapshot expectedTarget);
}

internal sealed class WindowsWhisperTargetReadObservation
{
    internal WindowsWhisperTargetReadObservation(
        string text,
        WhisperVerificationMethod method)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Method = method;
    }

    internal string Text { get; }

    internal WhisperVerificationMethod Method { get; }
}

internal sealed class WindowsUiAutomationTargetTextBackend :
    IWindowsWhisperTargetTextBackend
{
    public WindowsWhisperTargetReadObservation? ReadFocused(
        WhisperTargetSnapshot expectedTarget)
    {
        AutomationElement? element = AutomationElement.FocusedElement;
        if (element is null ||
            expectedTarget.Context.IsPassword ||
            !WindowsWhisperAutomationIdentity.Matches(
                element,
                expectedTarget.Identity))
        {
            return null;
        }

        AutomationElement.AutomationElementInformation current = element.Current;
        if (!current.IsEnabled || current.IsPassword)
        {
            return null;
        }

        WindowsWhisperTargetReadObservation? observation =
            TryReadTextPattern(element) ??
            TryReadValuePattern(element);
        if (observation is null ||
            !WindowsWhisperAutomationIdentity.Matches(
                element,
                expectedTarget.Identity))
        {
            return null;
        }

        return observation;
    }

    private static WindowsWhisperTargetReadObservation? TryReadTextPattern(
        AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(
                TextPattern.Pattern,
                out object? textObject) ||
            textObject is not TextPattern textPattern)
        {
            return null;
        }

        string text = textPattern.DocumentRange.GetText(
            WhisperLimits.MaximumReadbackCharacters + 1);
        return text.Length > WhisperLimits.MaximumReadbackCharacters
            ? null
            : new WindowsWhisperTargetReadObservation(
                text,
                WhisperVerificationMethod.AutomationTextRead);
    }

    private static WindowsWhisperTargetReadObservation? TryReadValuePattern(
        AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out object? valueObject) ||
            valueObject is not ValuePattern valuePattern)
        {
            return null;
        }

        string text = valuePattern.Current.Value;
        return text.Length > WhisperLimits.MaximumReadbackCharacters
            ? null
            : new WindowsWhisperTargetReadObservation(
                text,
                WhisperVerificationMethod.AutomationValueRead);
    }
}

internal static class WindowsWhisperAutomationIdentity
{
    internal static bool Matches(
        AutomationElement element,
        WhisperTargetIdentity identity) =>
        element.Current.ProcessId == identity.ProcessId &&
        string.Equals(
            string.Join(
                '.',
                element.GetRuntimeId().Select(value => value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))),
            identity.ElementRuntimeId,
            StringComparison.Ordinal);
}
