using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace Soltex.Update;

public sealed class BoundedHttpsAcquirer
{
    private const int CopyBufferBytes = 64 * 1024;
    private static readonly string[] ArtifactOrder = ["manifest", "signature", "package"];
    private readonly IAuthenticatedHttpsTransport _transport;

    public BoundedHttpsAcquirer(IAuthenticatedHttpsTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    public async Task<AcquiredUpdateBundle> AcquireAsync(
        VerifiedUpdateDescriptor verified,
        UpdateTrustPolicy trustPolicy,
        string stagingParent,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ValidatedUpdateTrustPolicy policy = UpdateTrustPolicyValidator.Validate(
            trustPolicy,
            nowUtc);
        string token = Guid.NewGuid().ToString("N");
        string root = UpdatePrivateStaging.CreatePrivateRoot(stagingParent, token);
        Dictionary<string, AcquiredUpdateArtifact> acquired = new(StringComparer.Ordinal);
        Dictionary<string, FileStream> locks = new(StringComparer.Ordinal);
        try
        {
            using CancellationTokenSource overall =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overall.CancelAfter(TimeSpan.FromSeconds(
                verified.Descriptor.OverallTimeoutSeconds));
            foreach (string name in ArtifactOrder)
            {
                UpdateArtifactDescriptor descriptor = verified.Descriptor.Artifacts.Single(
                    item => string.Equals(item.Name, name, StringComparison.Ordinal));
                (AcquiredUpdateArtifact artifact, FileStream lockedStream) =
                    await DownloadArtifactAsync(
                        descriptor,
                        verified,
                        policy,
                        root,
                        overall.Token).ConfigureAwait(false);
                acquired.Add(name, artifact);
                locks.Add(name, lockedStream);
            }

            AcquiredUpdateBundle bundle = new(root, acquired, locks);
            foreach (string name in ArtifactOrder)
            {
                await bundle.VerifyArtifactUnchangedAsync(name, overall.Token)
                    .ConfigureAwait(false);
            }

            return bundle;
        }
        catch
        {
            foreach (FileStream stream in locks.Values)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            UpdatePrivateStaging.DeleteTree(root);
            throw;
        }
    }

    private async Task<(AcquiredUpdateArtifact Artifact, FileStream LockedStream)> DownloadArtifactAsync(
        UpdateArtifactDescriptor descriptor,
        VerifiedUpdateDescriptor verified,
        ValidatedUpdateTrustPolicy policy,
        string root,
        CancellationToken cancellationToken)
    {
        Uri initialUri = verified.ArtifactUris[descriptor.Name];
        Uri currentUri = initialUri;
        List<Uri> redirects = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int redirectCount = 0; ; redirectCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string origin = UpdateUri.NormalizeOrigin(currentUri);
            if (!verified.AllowedOrigins.Contains(origin) ||
                !policy.ActiveTlsPins.TryGetValue(origin, out IReadOnlySet<string>? pins))
            {
                throw new InvalidDataException(
                    "An update redirect left the signed origin and pin allowlist.");
            }

            await using AuthenticatedHttpsResponse response = await _transport.OpenAsync(
                currentUri,
                pins,
                TimeSpan.FromSeconds(verified.Descriptor.HeaderTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            if (!pins.Contains(response.TlsSpkiSha256) ||
                !string.Equals(
                    UpdateUri.NormalizeOrigin(response.EffectiveUri),
                    origin,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The HTTPS transport returned inconsistent origin or TLS pin evidence.");
            }

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= verified.Descriptor.MaximumRedirects ||
                    response.RedirectLocation is null)
                {
                    throw new InvalidDataException(
                        "The update response exceeded its redirect bound or omitted a location.");
                }

                Uri next = response.RedirectLocation.IsAbsoluteUri
                    ? response.RedirectLocation
                    : new Uri(currentUri, response.RedirectLocation);
                string nextOrigin = UpdateUri.NormalizeOrigin(next);
                if (!verified.AllowedOrigins.Contains(nextOrigin))
                {
                    throw new InvalidDataException(
                        "The update response redirected to an unauthorized origin.");
                }

                redirects.Add(next);
                currentUri = next;
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidDataException(
                    $"The update origin returned HTTP status {(int)response.StatusCode}.");
            }

            if (response.ContentLength != descriptor.Length)
            {
                throw new InvalidDataException(
                    "The update response Content-Length does not match the signed descriptor.");
            }

            string destination = Path.Combine(root, descriptor.Name + ".bin");
            (long length, string hash) = await CopyBoundedAsync(
                response.Content,
                destination,
                descriptor.Length,
                TimeSpan.FromSeconds(verified.Descriptor.ReadTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded update artifact SHA-256 does not match the signed descriptor.");
            }

            FileStream lockedStream = await OpenVerifiedReadLockAsync(
                destination,
                length,
                hash,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            AcquiredUpdateArtifact artifact = new(
                descriptor.Name,
                destination,
                length,
                hash,
                initialUri,
                currentUri,
                redirects.AsReadOnly(),
                response.TlsSpkiSha256,
                stopwatch.Elapsed);
            return (artifact, lockedStream);
        }
    }

    private static async Task<FileStream> OpenVerifiedReadLockAsync(
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                stream.Length != expectedLength)
            {
                throw new InvalidDataException(
                    "An acquired update artifact changed before its immutable read lock was established.");
            }

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            if (stream.Position != expectedLength ||
                !string.Equals(
                    Convert.ToHexString(hash),
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "An acquired update artifact changed before its immutable read lock was established.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<(long Length, string Sha256)> CopyBoundedAsync(
        Stream input,
        string destination,
        long expectedLength,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
    {
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        long actual = 0;
        try
        {
            while (true)
            {
                using CancellationTokenSource readCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readCancellation.CancelAfter(readTimeout);
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, CopyBufferBytes),
                    readCancellation.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                actual = checked(actual + read);
                if (actual > expectedLength)
                {
                    throw new InvalidDataException(
                        "The update response exceeded the signed byte length.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }

            if (actual != expectedLength)
            {
                throw new InvalidDataException(
                    "The update response ended before the signed byte length.");
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            return (actual, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}

internal static class UpdatePrivateStaging
{
    private const string Prefix = "soltex-update-";

    internal static string CreatePrivateRoot(string stagingParent, string token)
    {
        string parent = NormalizeOrCreateParent(stagingParent);
        ValidateToken(token);
        string root = Path.Combine(parent, Prefix + token);
        Directory.CreateDirectory(root);
        RejectReparsePoint(root);
        return root;
    }

    internal static string GetRootForToken(string stagingParent, string token)
    {
        ValidateToken(token);
        return Path.Combine(Path.GetFullPath(stagingParent), Prefix + token);
    }

    internal static bool IsToken(string? token) =>
        token is { Length: 32 } &&
        token.All(character =>
            character is >= '0' and <= '9' or
            >= 'a' and <= 'f');

    internal static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        FileAttributes rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(root);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (directory && !reparse)
            {
                DeleteTree(entry);
            }
            else if (directory)
            {
                Directory.Delete(entry);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }

        Directory.Delete(root);
    }

    private static string NormalizeOrCreateParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        EnsureNoReparseComponents(full);
        Directory.CreateDirectory(full);
        EnsureNoReparseComponents(full);
        return full;
    }

    private static void EnsureNoReparseComponents(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException("The private staging parent has no path root.");
        }

        string current = root;
        foreach (string segment in Path.GetRelativePath(root, fullPath).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Reparse-point update staging directories are not accepted.");
        }
    }

    private static void ValidateToken(string token)
    {
        if (!IsToken(token))
        {
            throw new ArgumentException("The private update staging token is invalid.", nameof(token));
        }
    }
}
