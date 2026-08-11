using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace Soltex.App;

internal enum ProcessActionStatus
{
    Rejected,
    AlreadyExited,
    Closed,
    NeedsForceConfirmation,
    ForceStopped,
    Failed
}

internal sealed record ProcessActionRequest(int ProcessId, string ExpectedName);

internal sealed record ProcessActionTicket(
    int ProcessId,
    string ExpectedName,
    int SessionId,
    long StartTimeUtcTicks);

internal sealed record ProcessActionResult(
    ProcessActionStatus Status,
    string Message,
    ProcessActionTicket? Ticket = null);

internal sealed record ProcessActionPolicyDecision(bool Allowed, string Reason);

internal static class ProcessActionPolicy
{
    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Registry",
        "Secure System",
        "Memory Compression",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "winlogon",
        "fontdrvhost",
        "dwm",
        "sihost",
        "MsMpEng",
        "SecurityHealthService",
        "LsaIso"
    };

    internal static ProcessActionPolicyDecision EvaluateTarget(
        ProcessActionRequest request,
        int ownProcessId,
        int currentSessionId,
        int targetSessionId,
        string actualName)
    {
        if (request.ProcessId <= 4)
        {
            return Deny("Windows system processes cannot be ended from Soltex.");
        }

        if (request.ProcessId == ownProcessId)
        {
            return Deny("Soltex cannot end its own process.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedName) ||
            string.IsNullOrWhiteSpace(actualName) ||
            !string.Equals(request.ExpectedName, actualName, StringComparison.OrdinalIgnoreCase))
        {
            return Deny("The selected process identity changed. Refresh the list and select it again.");
        }

        if (CriticalProcessNames.Contains(actualName))
        {
            return Deny("This Windows security or session process is protected from Soltex actions.");
        }

        if (targetSessionId == 0)
        {
            return Deny("Session 0 processes cannot be ended from Soltex.");
        }

        if (targetSessionId != currentSessionId)
        {
            return Deny("Processes in another Windows session cannot be ended from Soltex.");
        }

        return new ProcessActionPolicyDecision(true, string.Empty);
    }

    internal static ProcessActionPolicyDecision RevalidateTicket(
        ProcessActionTicket ticket,
        int ownProcessId,
        int currentSessionId,
        int targetSessionId,
        string actualName,
        long actualStartTimeUtcTicks)
    {
        ProcessActionPolicyDecision target = EvaluateTarget(
            new ProcessActionRequest(ticket.ProcessId, ticket.ExpectedName),
            ownProcessId,
            currentSessionId,
            targetSessionId,
            actualName);
        if (!target.Allowed)
        {
            return target;
        }

        if (ticket.SessionId != targetSessionId ||
            ticket.StartTimeUtcTicks != actualStartTimeUtcTicks)
        {
            return Deny("The selected process instance changed. Refresh the list and select it again.");
        }

        return new ProcessActionPolicyDecision(true, string.Empty);
    }

    private static ProcessActionPolicyDecision Deny(string reason) => new(false, reason);
}

internal sealed class ProcessActionService
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForceStopTimeout = TimeSpan.FromSeconds(2);

    internal async Task<ProcessActionResult> RequestCloseAsync(
        ProcessActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatedProcess opened = OpenAndValidate(request, null);
        if (opened.Result is not null)
        {
            return opened.Result;
        }

        using Process process = opened.Process!;
        ProcessActionTicket ticket = opened.Ticket!;
        try
        {
            if (process.HasExited)
            {
                return AlreadyExited(request);
            }

            bool requested = process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow();
            if (requested &&
                await WaitForExitBoundedAsync(process, GracefulCloseTimeout, cancellationToken).ConfigureAwait(false))
            {
                return new ProcessActionResult(
                    ProcessActionStatus.Closed,
                    $"{ticket.ExpectedName} closed normally.");
            }

            return new ProcessActionResult(
                ProcessActionStatus.NeedsForceConfirmation,
                $"{ticket.ExpectedName} did not close. Force stop ends only PID {ticket.ProcessId}; unsaved work may be lost.",
                ticket);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProcessActionResult(ProcessActionStatus.Failed, "The process action was canceled.");
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            return new ProcessActionResult(
                ProcessActionStatus.Failed,
                $"Windows did not allow Soltex to close {request.ExpectedName}.");
        }
    }

    internal async Task<ProcessActionResult> ForceStopAsync(
        ProcessActionTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ProcessActionRequest request = new(ticket.ProcessId, ticket.ExpectedName);
        ValidatedProcess opened = OpenAndValidate(request, ticket);
        if (opened.Result is not null)
        {
            return opened.Result;
        }

        using Process process = opened.Process!;
        try
        {
            if (process.HasExited)
            {
                return AlreadyExited(request);
            }

            // Deliberately target only the selected process. Descendants are never included.
            process.Kill(entireProcessTree: false);
            if (await WaitForExitBoundedAsync(process, ForceStopTimeout, cancellationToken).ConfigureAwait(false))
            {
                return new ProcessActionResult(
                    ProcessActionStatus.ForceStopped,
                    $"{ticket.ExpectedName} was force stopped.");
            }

            return new ProcessActionResult(
                ProcessActionStatus.Failed,
                $"Windows accepted the request, but {ticket.ExpectedName} did not exit within two seconds.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProcessActionResult(ProcessActionStatus.Failed, "The process action was canceled.");
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            return new ProcessActionResult(
                ProcessActionStatus.Failed,
                $"Windows did not allow Soltex to force stop {ticket.ExpectedName}.");
        }
    }

    private static ValidatedProcess OpenAndValidate(
        ProcessActionRequest request,
        ProcessActionTicket? ticket)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(request.ProcessId);
            process.Refresh();
            string actualName = process.ProcessName;
            int targetSessionId = process.SessionId;
            long startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            using Process current = Process.GetCurrentProcess();
            int currentSessionId = current.SessionId;

            ProcessActionPolicyDecision decision = ticket is null
                ? ProcessActionPolicy.EvaluateTarget(
                    request,
                    Environment.ProcessId,
                    currentSessionId,
                    targetSessionId,
                    actualName)
                : ProcessActionPolicy.RevalidateTicket(
                    ticket,
                    Environment.ProcessId,
                    currentSessionId,
                    targetSessionId,
                    actualName,
                    startTimeUtcTicks);
            if (!decision.Allowed)
            {
                process.Dispose();
                return new ValidatedProcess(
                    null,
                    null,
                    new ProcessActionResult(ProcessActionStatus.Rejected, decision.Reason));
            }

            return new ValidatedProcess(
                process,
                new ProcessActionTicket(
                    request.ProcessId,
                    actualName,
                    targetSessionId,
                    startTimeUtcTicks),
                null);
        }
        catch (ArgumentException)
        {
            process?.Dispose();
            return new ValidatedProcess(null, null, AlreadyExited(request));
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            process?.Dispose();
            return new ValidatedProcess(
                null,
                null,
                new ProcessActionResult(
                    ProcessActionStatus.Rejected,
                    $"Windows did not expose enough identity information to act on {request.ExpectedName}."));
        }
    }

    private static async Task<bool> WaitForExitBoundedAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private static ProcessActionResult AlreadyExited(ProcessActionRequest request) =>
        new(ProcessActionStatus.AlreadyExited, $"{request.ExpectedName} is no longer running.");

    private static bool IsExpectedProcessFailure(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            NotSupportedException or
            PlatformNotSupportedException or
            SecurityException or
            UnauthorizedAccessException;

    private sealed record ValidatedProcess(
        Process? Process,
        ProcessActionTicket? Ticket,
        ProcessActionResult? Result);
}
