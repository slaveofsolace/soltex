using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace Soltex.RemoteAssist;

public enum RemoteAssistMode
{
    ShareThisDevice,
    ControlPeer
}

public sealed class RemoteAssistLaunchPlan
{
    internal RemoteAssistLaunchPlan(
        RemoteAssistExecutable executable,
        RemoteAssistMode mode,
        IEnumerable<string> arguments)
    {
        Executable = executable;
        Mode = mode;
        Arguments = new ReadOnlyCollection<string>(arguments.ToArray());
    }

    public RemoteAssistExecutable Executable { get; }

    public RemoteAssistMode Mode { get; }

    public IReadOnlyList<string> Arguments { get; }
}

public sealed record RemoteAssistLaunchResult(
    bool Started,
    int? ProcessId,
    DateTimeOffset AttemptedAtUtc,
    string Message);

public static class RustDeskExternalClient
{
    public static RemoteAssistLaunchPlan CreateSharePlan(RemoteAssistExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return new RemoteAssistLaunchPlan(executable, RemoteAssistMode.ShareThisDevice, []);
    }

    public static RemoteAssistLaunchPlan CreateControlPlan(
        RemoteAssistExecutable executable,
        RemotePeerId peerId)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(peerId);
        return new RemoteAssistLaunchPlan(
            executable,
            RemoteAssistMode.ControlPeer,
            ["--connect", peerId.Value]);
    }

    public static ProcessStartInfo CreateStartInfo(RemoteAssistLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ProcessStartInfo startInfo = new(plan.Executable.FullPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(plan.Executable.FullPath) ?? string.Empty
        };
        foreach (string argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static RemoteAssistLaunchResult Launch(RemoteAssistLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        DateTimeOffset attemptedAtUtc = DateTimeOffset.UtcNow;
        if (!plan.Executable.VerifyUnchanged(out string validationError))
        {
            return new RemoteAssistLaunchResult(
                Started: false,
                ProcessId: null,
                AttemptedAtUtc: attemptedAtUtc,
                Message: validationError);
        }

        try
        {
            using Process? process = Process.Start(CreateStartInfo(plan));
            return process is null
                ? new RemoteAssistLaunchResult(
                    Started: false,
                    ProcessId: null,
                    AttemptedAtUtc: attemptedAtUtc,
                    Message: "Windows did not start the external RustDesk client.")
                : new RemoteAssistLaunchResult(
                    Started: true,
                    ProcessId: process.Id,
                    AttemptedAtUtc: attemptedAtUtc,
                    Message: "RustDesk was launched. Transport, authentication, consent, and session state remain owned by RustDesk.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return new RemoteAssistLaunchResult(
                Started: false,
                ProcessId: null,
                AttemptedAtUtc: attemptedAtUtc,
                Message: exception.Message);
        }
    }
}
