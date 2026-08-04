namespace Soltex.Security;

internal static class AuthenticatedStateFileIO
{
    internal static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException(
                "Authenticated state size is outside the accepted bounds.");
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = await stream.ReadAsync(
                bytes.AsMemory(offset, bytes.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Authenticated state ended unexpectedly.");
            }

            offset += read;
        }

        byte[] extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Authenticated state grew while it was read.");
        }

        return bytes;
    }

    internal static async Task WriteDurableAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    internal static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Reparse-point authenticated-state files are not accepted.");
        }
    }

    internal static string CreateTemporaryPath(string destination) =>
        destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
