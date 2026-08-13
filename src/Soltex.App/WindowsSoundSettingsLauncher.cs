using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace Soltex.App;

internal sealed record WindowsSoundSettingsLaunchPlan(
    string FileName,
    bool UseShellExecute,
    bool ErrorDialog);

internal sealed record WindowsSoundSettingsLaunchResult(
    bool Started,
    string Message);

internal interface IWindowsSoundSettingsLaunchBackend
{
    void Launch(WindowsSoundSettingsLaunchPlan plan);
}

internal sealed class ProcessWindowsSoundSettingsLaunchBackend : IWindowsSoundSettingsLaunchBackend
{
    public void Launch(WindowsSoundSettingsLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = plan.FileName,
            UseShellExecute = plan.UseShellExecute,
            ErrorDialog = plan.ErrorDialog
        });
    }
}

internal static class WindowsSoundSettingsLauncher
{
    internal const string SoundSettingsUri = "ms-settings:sound";

    internal static WindowsSoundSettingsLaunchPlan CreatePlan() =>
        new(SoundSettingsUri, UseShellExecute: true, ErrorDialog: false);

    internal static WindowsSoundSettingsLaunchResult Open(
        IWindowsSoundSettingsLaunchBackend? backend = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsSoundSettingsLaunchResult(
                Started: false,
                "Windows Sound settings are unavailable on this platform.");
        }

        try
        {
            (backend ?? new ProcessWindowsSoundSettingsLaunchBackend()).Launch(CreatePlan());
            return new WindowsSoundSettingsLaunchResult(
                Started: true,
                "Windows Sound settings launch was requested. Choose the system default there, then return and refresh.");
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or SecurityException or PlatformNotSupportedException)
        {
            return new WindowsSoundSettingsLaunchResult(
                Started: false,
                "Windows Sound settings could not be opened. No audio setting was changed.");
        }
    }
}
