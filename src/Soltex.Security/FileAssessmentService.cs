namespace Soltex.Security;

public sealed class FileAssessmentService
{
    public const long MaximumAmsiBytes = 16 * 1024 * 1024;

    private static readonly HashSet<string> HighRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appx", ".bat", ".cmd", ".com", ".dll", ".exe", ".hta", ".js", ".jse", ".lnk",
        ".msi", ".msix", ".msp", ".ps1", ".reg", ".scr", ".sys", ".url", ".vbs", ".wsf"
    };

    private readonly IContentScanner _scanner;
    private readonly AllowListStore _allowList;

    public FileAssessmentService(IContentScanner scanner, AllowListStore allowList)
    {
        _scanner = scanner;
        _allowList = allowList;
    }

    public async Task<FileAssessment> AssessAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = PathSafety.NormalizeExistingFile(path);
        FileInfo file = new(fullPath);
        string hash = await FileHashing.Sha256Async(fullPath, cancellationToken).ConfigureAwait(false);

        if (await _allowList.ContainsAsync(hash, cancellationToken).ConfigureAwait(false))
        {
            return new FileAssessment(
                fullPath,
                hash,
                file.Length,
                ContentVerdict.Allowed,
                "Soltex allow list",
                "This exact SHA-256 hash was deliberately allowed by the user.",
                DateTimeOffset.UtcNow);
        }

        if (file.Length > MaximumAmsiBytes)
        {
            return new FileAssessment(
                fullPath,
                hash,
                file.Length,
                ContentVerdict.ReviewRecommended,
                _scanner.EngineName,
                "The file exceeds Soltex's bounded in-memory AMSI limit. Use the Defender custom scan before importing it.",
                DateTimeOffset.UtcNow);
        }

        byte[] content = new byte[checked((int)file.Length)];
        await using (FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        }

        ContentScanResult scan = _scanner.Scan(content, file.Name);
        if (scan.Verdict == ContentVerdict.Clean && HighRiskExtensions.Contains(file.Extension))
        {
            return new FileAssessment(
                fullPath,
                hash,
                file.Length,
                ContentVerdict.ReviewRecommended,
                scan.Engine,
                "No malware was detected, but executable content must also pass Soltex's signed-manifest policy.",
                DateTimeOffset.UtcNow);
        }

        return new FileAssessment(
            fullPath,
            hash,
            file.Length,
            scan.Verdict,
            scan.Engine,
            scan.Detail,
            DateTimeOffset.UtcNow);
    }

    public static bool ShouldInspectExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return HighRiskExtensions.Contains(extension) || extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".preset", StringComparison.OrdinalIgnoreCase);
    }
}
