using System.Security.Cryptography;

namespace WaveSlate.RemoteAssist;

public sealed record RemoteAssistExecutable
{
    private RemoteAssistExecutable(string fullPath, string sha256)
    {
        FullPath = fullPath;
        Sha256 = sha256;
    }

    public string FullPath { get; }

    public string Sha256 { get; }

    public static bool TryCreate(
        string? candidate,
        out RemoteAssistExecutable? executable,
        out string error)
    {
        executable = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "Select the external RustDesk executable first.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The selected executable path is invalid.";
            return false;
        }

        if (!string.Equals(
                Path.GetFileName(fullPath),
                "RustDesk.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "WaveSlate only launches an explicitly selected RustDesk.exe external client.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = "The selected RustDesk executable does not exist.";
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = "Reparse-point executable paths are not accepted.";
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "WaveSlate could not inspect the selected executable.";
            return false;
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            executable = new RemoteAssistExecutable(
                fullPath,
                Convert.ToHexString(SHA256.HashData(stream)));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "WaveSlate could not fingerprint the selected external client.";
            return false;
        }
    }

    public bool VerifyUnchanged(out string error)
    {
        if (!File.Exists(FullPath))
        {
            error = "The selected RustDesk executable is no longer present.";
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(FullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = "The selected executable became a reparse point after approval.";
                return false;
            }

            using FileStream stream = new(
                FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            byte[] expected = Convert.FromHexString(Sha256);
            byte[] actual = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                error = "The selected RustDesk executable changed after it was approved. Select it again before launching.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            error = "WaveSlate could not revalidate the selected external client.";
            return false;
        }
    }
}
