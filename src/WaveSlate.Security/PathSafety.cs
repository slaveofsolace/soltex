namespace WaveSlate.Security;

public static class PathSafety
{
    public static string NormalizeExistingDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        DirectoryInfo directory = new(fullPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException("The directory does not exist.");
        }

        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(pathRoot))
        {
            throw new InvalidDataException("The directory has no path root.");
        }

        string relative = Path.GetRelativePath(pathRoot, fullPath);
        string current = pathRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Reparse-point directories are not accepted at this trust boundary.");
            }
        }

        return fullPath;
    }

    public static string NormalizeExistingFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        FileInfo file = new(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The file does not exist.", fullPath);
        }

        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Reparse-point files are not accepted at this trust boundary.");
        }

        return fullPath;
    }

    public static string CombineUnderRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Manifest paths must be relative.");
        }

        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A path escaped the expected root.");
        }

        return candidate;
    }

    public static bool IsUnderRoot(string root, string candidate)
    {
        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        string fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
