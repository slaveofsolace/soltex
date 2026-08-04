using System.Buffers;
using System.Security.Cryptography;

namespace WaveSlate.Security;

internal sealed class LockedPublisherFile : IAsyncDisposable, IDisposable
{
    private const int CopyBufferBytes = 128 * 1024;
    private const long MaximumPublisherFileBytes = 4L * 1024 * 1024 * 1024;
    private readonly FileStream _snapshotStream;
    private readonly string _snapshotRoot;
    private bool _disposed;

    private LockedPublisherFile(
        string originalPath,
        string snapshotPath,
        string snapshotRoot,
        FileStream snapshotStream,
        long length,
        string sha256)
    {
        OriginalPath = originalPath;
        FullPath = snapshotPath;
        _snapshotRoot = snapshotRoot;
        _snapshotStream = snapshotStream;
        Length = length;
        Sha256 = sha256;
    }

    internal string OriginalPath { get; }
    internal string FullPath { get; }
    internal long Length { get; }
    internal string Sha256 { get; }

    internal static async Task<LockedPublisherFile> OpenAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string originalPath = PathSafety.NormalizeExistingFile(path);
        string snapshotRoot = Path.Combine(
            Path.GetTempPath(),
            "Soltex.PublisherVerification",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotRoot);
        RejectReparseDirectory(snapshotRoot);
        string snapshotPath = Path.Combine(snapshotRoot, "candidate.exe");
        FileStream? snapshotStream = null;
        try
        {
            await using FileStream source = new(
                originalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if ((File.GetAttributes(originalPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Reparse-point files are not accepted for publisher authorization.");
            }

            snapshotStream = new FileStream(
                snapshotPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                CopyBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            (long length, string sha256) = await CopyAndHashAsync(
                source,
                snapshotStream,
                cancellationToken).ConfigureAwait(false);
            await snapshotStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            snapshotStream.Flush(flushToDisk: true);
            snapshotStream.Position = 0;
            return new LockedPublisherFile(
                originalPath,
                snapshotPath,
                snapshotRoot,
                snapshotStream,
                length,
                sha256);
        }
        catch
        {
            if (snapshotStream is not null)
            {
                await snapshotStream.DisposeAsync().ConfigureAwait(false);
            }

            TryDeleteSnapshot(snapshotRoot);
            throw;
        }
    }

    internal async Task<bool> VerifyHashUnchangedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string current = await HashSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(Sha256, current, StringComparison.Ordinal);
    }

    private static async Task<(long Length, string Sha256)> CopyAndHashAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        long length = 0;
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, CopyBufferBytes),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length = checked(length + read);
                if (length > MaximumPublisherFileBytes)
                {
                    throw new InvalidDataException(
                        "The publisher-verification file exceeds the accepted size bound.");
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }

            if (length == 0)
            {
                throw new InvalidDataException(
                    "The publisher-verification file is empty.");
            }

            return (length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async Task<string> HashSnapshotAsync(CancellationToken cancellationToken)
    {
        _snapshotStream.Position = 0;
        byte[] hash = await SHA256.HashDataAsync(_snapshotStream, cancellationToken)
            .ConfigureAwait(false);
        if (_snapshotStream.Position != Length || _snapshotStream.Length != Length)
        {
            throw new IOException(
                "The immutable publisher snapshot length changed unexpectedly.");
        }

        _snapshotStream.Position = 0;
        return Convert.ToHexString(hash);
    }

    private static void RejectReparseDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "A reparse-point publisher snapshot directory is not accepted.");
        }
    }

    private static void TryDeleteSnapshot(string snapshotRoot)
    {
        try
        {
            if (Directory.Exists(snapshotRoot))
            {
                Directory.Delete(snapshotRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _snapshotStream.Dispose();
        TryDeleteSnapshot(_snapshotRoot);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _snapshotStream.DisposeAsync().ConfigureAwait(false);
        TryDeleteSnapshot(_snapshotRoot);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
