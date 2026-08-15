using System.Diagnostics;
using System.Threading.Channels;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public enum WhisperTargetInspectionFailureKind
{
    None,
    UnknownTarget,
    TimedOut,
    ProviderUnavailable
}

/// <summary>
/// Runs focused-control inspection away from the caller and fails closed when a
/// UI Automation provider does not answer promptly. At most one native inspection
/// can remain in flight, so a hostile provider cannot create an unbounded thread
/// or work queue inside Soltex.
/// </summary>
public sealed class WindowsWhisperTargetInspector : IWhisperTargetInspector
{
    public static TimeSpan DefaultInspectionTimeout { get; } = TimeSpan.FromMilliseconds(750);

    private readonly IWhisperTargetInspectionBackend _backend;
    private readonly TimeSpan _timeout;
    private readonly Channel<bool> _inspectionPermit = CreateInspectionPermit();
    private int _lastFailure;

    public WindowsWhisperTargetInspector()
        : this(new WindowsUiAutomationTargetBackend(), DefaultInspectionTimeout)
    {
    }

    internal WindowsWhisperTargetInspector(
        IWhisperTargetInspectionBackend backend,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Target inspection timeouts must be between 10 milliseconds and 5 seconds.");
        }

        _backend = backend;
        _timeout = timeout;
    }

    public WhisperTargetInspectionFailureKind LastFailure =>
        (WhisperTargetInspectionFailureKind)Volatile.Read(ref _lastFailure);

    public async ValueTask<WhisperTargetSnapshot?> InspectAsync(
        CancellationToken cancellationToken)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        using CancellationTokenSource permitDeadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        permitDeadline.CancelAfter(_timeout);
        try
        {
            _ = await _inspectionPermit.Reader
                .ReadAsync(permitDeadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetFailure(WhisperTargetInspectionFailureKind.TimedOut);
            return null;
        }

        Task<WindowsWhisperTargetObservation>? inspection = null;
        bool releaseDeferred = false;
        try
        {
            TimeSpan remaining = _timeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                SetFailure(WhisperTargetInspectionFailureKind.TimedOut);
                return null;
            }

            inspection = StartInspection();
            WindowsWhisperTargetObservation observation = await inspection
                .WaitAsync(remaining, cancellationToken)
                .ConfigureAwait(false);
            WhisperTargetSnapshot? snapshot = WindowsWhisperTargetMapper.Map(observation);
            SetFailure(snapshot is null
                ? WhisperTargetInspectionFailureKind.UnknownTarget
                : WhisperTargetInspectionFailureKind.None);
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (inspection is not null && !inspection.IsCompleted)
            {
                releaseDeferred = true;
                _ = ReleaseWhenCompleteAsync(inspection);
            }

            throw;
        }
        catch (TimeoutException)
        {
            SetFailure(WhisperTargetInspectionFailureKind.TimedOut);
            if (inspection is not null && !inspection.IsCompleted)
            {
                releaseDeferred = true;
                _ = ReleaseWhenCompleteAsync(inspection);
            }

            return null;
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception))
        {
            SetFailure(WhisperTargetInspectionFailureKind.ProviderUnavailable);
            return null;
        }
        finally
        {
            if (!releaseDeferred)
            {
                ReleaseInspectionPermit();
            }
        }
    }

    private Task<WindowsWhisperTargetObservation> StartInspection()
    {
        TaskCompletionSource<WindowsWhisperTargetObservation> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            try
            {
                completion.TrySetResult(_backend.InspectFocused());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Soltex Whisper target inspection"
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        return completion.Task;
    }

    private async Task ReleaseWhenCompleteAsync(Task inspection)
    {
        try
        {
            await inspection.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Observe any provider failure. Caller-visible state was already reduced
            // to a content-free timeout or cancellation outcome before this cleanup
            // continuation was scheduled.
        }
        finally
        {
            ReleaseInspectionPermit();
        }
    }

    private void ReleaseInspectionPermit()
    {
        if (!_inspectionPermit.Writer.TryWrite(true))
        {
            throw new InvalidOperationException("The target inspection permit was released twice.");
        }
    }

    private static Channel<bool> CreateInspectionPermit()
    {
        Channel<bool> permit = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        if (!permit.Writer.TryWrite(true))
        {
            throw new InvalidOperationException("The target inspection permit could not be initialized.");
        }

        return permit;
    }

    private void SetFailure(WhisperTargetInspectionFailureKind failure) =>
        Volatile.Write(ref _lastFailure, (int)failure);

    private static bool IsRecoverableProviderFailure(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}

internal interface IWhisperTargetInspectionBackend
{
    WindowsWhisperTargetObservation InspectFocused();
}

internal enum WindowsWhisperControlKind
{
    Unknown,
    Edit,
    Document,
    Text
}

internal sealed record WindowsWhisperTargetObservation(
    int ProcessId,
    string ProcessName,
    IReadOnlyList<int> RuntimeId,
    WhisperTargetIntegrityLevel IntegrityLevel,
    WindowsWhisperControlKind ControlKind,
    bool IsEnabled,
    bool IsKeyboardFocusable,
    bool IsPassword,
    bool SupportsValuePattern,
    bool ValueIsReadOnly,
    bool SupportsTextPattern,
    bool SupportsTextPattern2,
    bool TextReadOnlyKnown,
    bool TextIsReadOnly,
    bool SupportsSelection);

internal static class WindowsWhisperTargetMapper
{
    private static readonly HashSet<string> TerminalProcesses = new(
        ["windowsterminal", "openconsole", "conhost", "pwsh", "powershell", "cmd"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BrowserProcesses = new(
        ["chrome", "msedge", "firefox", "brave", "opera"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> EditorProcesses = new(
        ["code", "devenv", "notepad++", "sublime_text", "rider64"],
        StringComparer.OrdinalIgnoreCase);

    internal static WhisperTargetSnapshot? Map(WindowsWhisperTargetObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ProcessId <= 0 ||
            string.IsNullOrWhiteSpace(observation.ProcessName) ||
            observation.RuntimeId.Count == 0 ||
            observation.RuntimeId.Count > 32 ||
            observation.IntegrityLevel == WhisperTargetIntegrityLevel.Unknown)
        {
            return null;
        }

        string runtimeId = string.Join(
            '.',
            observation.RuntimeId.Select(value => value.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        if (runtimeId.Length is 0 or > WhisperTargetIdentity.MaximumRuntimeIdCharacters)
        {
            return null;
        }

        WhisperTargetKind kind = Classify(observation);
        if (kind == WhisperTargetKind.Unknown)
        {
            return null;
        }

        bool readOnly = observation.SupportsValuePattern
            ? observation.ValueIsReadOnly
            : !observation.TextReadOnlyKnown || observation.TextIsReadOnly;
        bool editable = observation.IsEnabled &&
            observation.IsKeyboardFocusable &&
            !observation.IsPassword &&
            !readOnly &&
            (observation.SupportsValuePattern || observation.SupportsTextPattern);

        WhisperTargetCapabilities capabilities = new(
            observation.SupportsValuePattern,
            observation.SupportsTextPattern,
            observation.SupportsTextPattern2,
            observation.SupportsSelection,
            observation.SupportsTextPattern2);
        bool elevated = observation.IntegrityLevel is
            WhisperTargetIntegrityLevel.High or
            WhisperTargetIntegrityLevel.System or
            WhisperTargetIntegrityLevel.Protected;
        WhisperTargetContext context = new(
            observation.ProcessName,
            kind,
            isKnown: true,
            isEditable: editable,
            isPassword: observation.IsPassword,
            isReadOnly: readOnly,
            isElevated: elevated,
            capabilities,
            observation.IntegrityLevel);

        return new WhisperTargetSnapshot(
            new WhisperTargetIdentity(
                observation.ProcessId,
                observation.ProcessName,
                runtimeId),
            context);
    }

    private static WhisperTargetKind Classify(WindowsWhisperTargetObservation observation)
    {
        string processName = observation.ProcessName;
        if (TerminalProcesses.Contains(processName))
        {
            return WhisperTargetKind.Terminal;
        }

        if (BrowserProcesses.Contains(processName))
        {
            return WhisperTargetKind.Browser;
        }

        if (EditorProcesses.Contains(processName))
        {
            return WhisperTargetKind.Editor;
        }

        return observation.ControlKind switch
        {
            WindowsWhisperControlKind.Edit when observation.IsPassword =>
                WhisperTargetKind.PlainText,
            WindowsWhisperControlKind.Edit when observation.SupportsValuePattern =>
                WhisperTargetKind.PlainText,
            WindowsWhisperControlKind.Document when observation.SupportsTextPattern =>
                WhisperTargetKind.RichText,
            WindowsWhisperControlKind.Text when observation.SupportsTextPattern =>
                WhisperTargetKind.RichText,
            _ => WhisperTargetKind.Unknown
        };
    }
}
