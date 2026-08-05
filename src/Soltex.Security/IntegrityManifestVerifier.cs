using System.Security.Cryptography;
using System.Text.Json;

namespace Soltex.Security;

public sealed record IntegrityManifest(
    int SchemaVersion,
    string Product,
    DateTimeOffset GeneratedAtUtc,
    List<IntegrityManifestFile> Files);

public sealed record IntegrityManifestFile(string Path, long Length, string Sha256);

public sealed record IntegrityVerificationResult(
    bool Succeeded,
    int VerifiedFileCount,
    IReadOnlyList<string> Errors);

public static class IntegrityManifestVerifier
{
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const int MaximumSignatureBytes = 16 * 1024;
    private const int MaximumFiles = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IntegrityVerificationResult> VerifyAsync(
        string contentRoot,
        string manifestPath,
        string signaturePath,
        string publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        string safeManifestPath = PathSafety.NormalizeExistingFile(manifestPath);
        string safeSignaturePath = PathSafety.NormalizeExistingFile(signaturePath);

        FileInfo manifestInfo = new(safeManifestPath);
        FileInfo signatureInfo = new(safeSignaturePath);
        if (manifestInfo.Length is <= 0 or > MaximumManifestBytes)
        {
            return Failure("The integrity manifest size is outside the accepted bounds.");
        }

        if (signatureInfo.Length is <= 0 or > MaximumSignatureBytes)
        {
            return Failure("The detached signature size is outside the accepted bounds.");
        }

        byte[] manifestBytes = await File.ReadAllBytesAsync(safeManifestPath, cancellationToken).ConfigureAwait(false);
        byte[] signatureBytes = await File.ReadAllBytesAsync(safeSignaturePath, cancellationToken).ConfigureAwait(false);

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            bool signatureValid = rsa.VerifyData(
                manifestBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            if (!signatureValid)
            {
                return Failure("The integrity manifest signature is invalid.");
            }
        }
        catch (CryptographicException exception)
        {
            return Failure($"The integrity key or signature is invalid: {exception.Message}");
        }

        IntegrityManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<IntegrityManifest>(manifestBytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Failure($"The integrity manifest JSON is invalid: {exception.Message}");
        }

        if (manifest is null ||
            !ProductIdentity.IsSupportedIntegrityManifestProduct(
                manifest.SchemaVersion,
                manifest.Product))
        {
            return Failure("The integrity manifest schema or product identifier is unsupported.");
        }

        if (manifest.Files.Count > MaximumFiles)
        {
            return Failure("The integrity manifest contains too many files.");
        }

        List<string> errors = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int verified = 0;
        foreach (IntegrityManifestFile item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(item.Path))
            {
                errors.Add($"Duplicate manifest path: {item.Path}");
                continue;
            }

            string filePath;
            try
            {
                filePath = PathSafety.CombineUnderRoot(contentRoot, item.Path);
                filePath = PathSafety.NormalizeExistingFile(filePath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                errors.Add($"{item.Path}: {exception.Message}");
                continue;
            }

            FileInfo file = new(filePath);
            if (file.Length != item.Length)
            {
                errors.Add($"{item.Path}: length mismatch.");
                continue;
            }

            string actualHash = await FileHashing.Sha256Async(filePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{item.Path}: SHA-256 mismatch.");
                continue;
            }

            verified++;
        }

        return new IntegrityVerificationResult(errors.Count == 0, verified, errors);
    }

    private static IntegrityVerificationResult Failure(string error) => new(false, 0, [error]);
}
