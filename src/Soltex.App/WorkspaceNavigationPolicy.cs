namespace Soltex.App;

internal enum WorkspaceNavigationTarget
{
    Home,
    Monitoring,
    Applications,
    Mixer,
    Security,
    Remote,
    Whisper,
    Activity,
    Updates,
    Settings
}

internal static class WorkspaceNavigationPolicy
{
    internal static bool TryResolve(string? workspace, out WorkspaceNavigationTarget target)
    {
        target = workspace switch
        {
            "home" => WorkspaceNavigationTarget.Home,
            "monitoring" => WorkspaceNavigationTarget.Monitoring,
            "applications" => WorkspaceNavigationTarget.Applications,
            "mixer" => WorkspaceNavigationTarget.Mixer,
            "security" => WorkspaceNavigationTarget.Security,
            "remote" => WorkspaceNavigationTarget.Remote,
            "whisper" => WorkspaceNavigationTarget.Whisper,
            "activity" => WorkspaceNavigationTarget.Activity,
            "updates" => WorkspaceNavigationTarget.Updates,
            "settings" => WorkspaceNavigationTarget.Settings,
            _ => WorkspaceNavigationTarget.Home
        };

        return workspace is
            "home" or
            "monitoring" or
            "applications" or
            "mixer" or
            "security" or
            "remote" or
            "whisper" or
            "activity" or
            "updates" or
            "settings";
    }
}
