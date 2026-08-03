namespace WaveSlate.RemoteAssist;

public static class RemoteAssistExecutableLocator
{
    public static RemoteAssistExecutable? FindInstalled()
    {
        foreach (string candidate in GetBoundedCandidates())
        {
            if (RemoteAssistExecutable.TryCreate(candidate, out RemoteAssistExecutable? executable, out _))
            {
                return executable;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetBoundedCandidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[] { programFiles, programFilesX86 }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path, "RustDesk", "RustDesk.exe"))
            .ToArray();
    }
}
