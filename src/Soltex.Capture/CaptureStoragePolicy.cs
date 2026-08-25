using System.Globalization;

namespace Soltex.Capture;

public sealed record CaptureStoragePlan(string RootPath, string FilePath, string FileName);

public static class CaptureStoragePolicy
{
    public const long MaximumClipBytes = 8L * 1_024 * 1_024 * 1_024;

    public static CaptureStoragePlan PlanOwnedFile(
        string rootPath,
        CaptureOutputKind kind,
        DateTimeOffset createdAtUtc,
        Guid clipId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (clipId == Guid.Empty)
        {
            throw new ArgumentException("A clip identifier is required.", nameof(clipId));
        }

        string root = Path.GetFullPath(rootPath);
        string extension = kind == CaptureOutputKind.Screenshot ? ".png" : ".mp4";
        string prefix = kind switch
        {
            CaptureOutputKind.Screenshot => "Screenshot",
            CaptureOutputKind.Replay => "Replay",
            _ => "Recording"
        };
        string fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{createdAtUtc:yyyyMMdd-HHmmss}-{clipId:N}{extension}");
        string filePath = Path.GetFullPath(Path.Combine(root, fileName));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The capture path escaped its owned root.");
        }

        return new CaptureStoragePlan(root, filePath, fileName);
    }

    internal static string NormalizeFileName(string? value, CaptureOutputKind kind)
    {
        string fallback = kind == CaptureOutputKind.Screenshot ? "Screenshot.png" : "Recording.mp4";
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string candidate = Path.GetFileName(value.Trim());
        if (candidate.Length == 0 ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(candidate, value.Trim(), StringComparison.Ordinal))
        {
            return fallback;
        }

        return candidate.Length <= ClipManifest.MaximumFileNameLength
            ? candidate
            : candidate[..ClipManifest.MaximumFileNameLength];
    }
}
