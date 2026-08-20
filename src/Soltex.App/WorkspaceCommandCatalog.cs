namespace Soltex.App;

internal sealed record WorkspaceCommand(
    string Workspace,
    string Label,
    string Group,
    string Shortcut,
    string SearchText);

internal static class WorkspaceCommandCatalog
{
    internal const int MaximumResults = 10;

    internal static IReadOnlyList<WorkspaceCommand> All { get; } =
    [
        new("home", "Overview", "System", "Ctrl+1", "home dashboard summary cpu memory network"),
        new("monitoring", "Performance", "System", "Ctrl+2", "monitoring telemetry cpu memory storage network processes"),
        new("applications", "Applications", "System", "Ctrl+3", "apps installed startup services inventory"),
        new("mixer", "Audio", "Control", "Ctrl+4", "audio mixer volume mute devices sessions"),
        new("security", "Security", "Control", "Ctrl+5", "security defender scan quarantine protection"),
        new("remote", "Remote Assist", "Control", "Ctrl+6", "remote rustdesk screen sharing support peer"),
        new("whisper", "Whisper", "Control", "Ctrl+0", "whisper dictation voice speech transcribe microphone shortcut scratchpad"),
        new("activity", "Activity", "Maintain", "Ctrl+7", "activity history events recovery"),
        new("updates", "Updates", "Maintain", "Ctrl+8", "updates release planning recovery"),
        new("settings", "Settings", "Maintain", "Ctrl+9", "settings preferences window activity boundaries")
    ];

    internal static IReadOnlyList<WorkspaceCommand> Query(string? query)
    {
        string[] terms = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return All;
        }

        return All
            .Where(command => terms.All(term =>
                command.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                command.Group.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                command.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(MaximumResults)
            .ToArray();
    }
}
