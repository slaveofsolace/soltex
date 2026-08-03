using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WaveSlate.Security;

public sealed record ApprovedPublisherIdentity
{
    public ApprovedPublisherIdentity(
        string displayName,
        string subjectNameSha256,
        string subjectPublicKeyInfoSha256)
    {
        DisplayName = NormalizeDisplayName(displayName);
        SubjectNameSha256 = NormalizeSha256(subjectNameSha256, nameof(subjectNameSha256));
        SubjectPublicKeyInfoSha256 = NormalizeSha256(
            subjectPublicKeyInfoSha256,
            nameof(subjectPublicKeyInfoSha256));
    }

    public string DisplayName { get; }
    public string SubjectNameSha256 { get; }
    public string SubjectPublicKeyInfoSha256 { get; }

    internal static string NormalizeSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "A publisher pin must be exactly one SHA-256 value encoded as hexadecimal.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The publisher display name is outside the accepted bounds.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed record PublisherIdentityEvidence(
    string Subject,
    string Issuer,
    string SerialNumber,
    string CertificateSha256,
    string SubjectNameSha256,
    string SubjectPublicKeyInfoSha256,
    bool HasCodeSigningEnhancedKeyUsage,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc)
{
    private const string CodeSigningEnhancedKeyUsageOid = "1.3.6.1.5.5.7.3.3";
    private const int MaximumDisplayFieldLength = 512;

    public static PublisherIdentityEvidence FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        bool hasCodeSigningUsage = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(usage => string.Equals(
                usage.Value,
                CodeSigningEnhancedKeyUsageOid,
                StringComparison.Ordinal));

        return new PublisherIdentityEvidence(
            Bound(certificate.Subject),
            Bound(certificate.Issuer),
            Bound(certificate.SerialNumber),
            Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            Convert.ToHexString(SHA256.HashData(certificate.SubjectName.RawData)),
            Convert.ToHexString(SHA256.HashData(
                certificate.PublicKey.ExportSubjectPublicKeyInfo())),
            hasCodeSigningUsage,
            certificate.NotBefore.ToUniversalTime(),
            certificate.NotAfter.ToUniversalTime());
    }

    private static string Bound(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Trim();
        return normalized.Length <= MaximumDisplayFieldLength
            ? normalized
            : normalized[..MaximumDisplayFieldLength];
    }
}

public enum PublisherPolicyStatus
{
    Approved,
    MissingCodeSigningUsage,
    PublisherNotApproved
}

public sealed record PublisherPolicyResult(
    PublisherPolicyStatus Status,
    string? ApprovedPublisher,
    string Detail)
{
    public bool IsApproved => Status == PublisherPolicyStatus.Approved;
}

public sealed class PublisherPolicy
{
    private const int MaximumApprovedPublishers = 32;
    private readonly IReadOnlyList<ApprovedPublisherIdentity> _approvedPublishers;

    public PublisherPolicy(IEnumerable<ApprovedPublisherIdentity> approvedPublishers)
    {
        ArgumentNullException.ThrowIfNull(approvedPublishers);
        List<ApprovedPublisherIdentity> approved = approvedPublishers.ToList();
        if (approved.Count is < 1 or > MaximumApprovedPublishers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvedPublishers),
                $"A publisher policy must contain between 1 and {MaximumApprovedPublishers} identities.");
        }

        if (approved
            .GroupBy(
                item => (item.SubjectNameSha256, item.SubjectPublicKeyInfoSha256),
                PublisherIdentityTupleComparer.Instance)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A publisher policy cannot contain duplicate subject/key identities.",
                nameof(approvedPublishers));
        }

        _approvedPublishers = approved.AsReadOnly();
    }

    public IReadOnlyList<ApprovedPublisherIdentity> ApprovedPublishers => _approvedPublishers;

    public PublisherPolicyResult Evaluate(PublisherIdentityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.HasCodeSigningEnhancedKeyUsage)
        {
            return new PublisherPolicyResult(
                PublisherPolicyStatus.MissingCodeSigningUsage,
                null,
                "The signer certificate does not explicitly permit code signing.");
        }

        ApprovedPublisherIdentity? approved = _approvedPublishers.FirstOrDefault(item =>
            string.Equals(
                item.SubjectNameSha256,
                evidence.SubjectNameSha256,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.SubjectPublicKeyInfoSha256,
                evidence.SubjectPublicKeyInfoSha256,
                StringComparison.OrdinalIgnoreCase));

        return approved is null
            ? new PublisherPolicyResult(
                PublisherPolicyStatus.PublisherNotApproved,
                null,
                "The trusted signer does not match an approved subject and public-key identity.")
            : new PublisherPolicyResult(
                PublisherPolicyStatus.Approved,
                approved.DisplayName,
                $"The signer matches the approved publisher '{approved.DisplayName}'.");
    }

    private sealed class PublisherIdentityTupleComparer :
        IEqualityComparer<(string SubjectNameSha256, string SubjectPublicKeyInfoSha256)>
    {
        internal static PublisherIdentityTupleComparer Instance { get; } = new();

        public bool Equals(
            (string SubjectNameSha256, string SubjectPublicKeyInfoSha256) left,
            (string SubjectNameSha256, string SubjectPublicKeyInfoSha256) right) =>
            string.Equals(left.SubjectNameSha256, right.SubjectNameSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.SubjectPublicKeyInfoSha256,
                right.SubjectPublicKeyInfoSha256,
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            (string SubjectNameSha256, string SubjectPublicKeyInfoSha256) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.SubjectNameSha256),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.SubjectPublicKeyInfoSha256));
    }
}

public sealed record AuthenticodeSignerIdentityResult(
    bool Succeeded,
    PublisherIdentityEvidence? Evidence,
    string Detail);

public static class AuthenticodeSignerIdentityReader
{
    public static AuthenticodeSignerIdentityResult Read(string path)
    {
        string fullPath = PathSafety.NormalizeExistingFile(path);
        if (!OperatingSystem.IsWindows())
        {
            return new AuthenticodeSignerIdentityResult(
                false,
                null,
                "Authenticode signer identity is only available on Windows.");
        }

        try
        {
#pragma warning disable SYSLIB0057 // No X509CertificateLoader equivalent extracts a signer from a signed PE file.
            using X509Certificate signer = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
            byte[] certificateBytes = signer.Export(X509ContentType.Cert);
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            return new AuthenticodeSignerIdentityResult(
                true,
                PublisherIdentityEvidence.FromCertificate(certificate),
                "The embedded Authenticode signer identity was read.");
        }
        catch (Exception exception) when (
            exception is CryptographicException or
            ArgumentException or
            IOException)
        {
            return new AuthenticodeSignerIdentityResult(
                false,
                null,
                "The file does not expose a readable Authenticode signer certificate.");
        }
    }
}

public enum AuthenticodePublisherStatus
{
    Approved,
    FileChangedDuringVerification,
    SignatureNotTrusted,
    SignerIdentityUnavailable,
    PublisherNotApproved,
    Error
}

public sealed record AuthenticodePublisherVerificationResult(
    AuthenticodePublisherStatus Status,
    string? FileSha256,
    AuthenticodeVerificationResult? Authenticode,
    PublisherIdentityEvidence? Signer,
    PublisherPolicyResult? Policy,
    string Detail)
{
    public bool IsApproved => Status == AuthenticodePublisherStatus.Approved;
}

public static class AuthenticodePublisherVerifier
{
    public static async Task<AuthenticodePublisherVerificationResult> VerifyAsync(
        string path,
        PublisherPolicy policy,
        bool allowNetworkRevocationRetrieval = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        try
        {
            string fullPath = PathSafety.NormalizeExistingFile(path);
            string hashBefore = await FileHashing.Sha256Async(fullPath, cancellationToken)
                .ConfigureAwait(false);
            AuthenticodeVerificationResult trust = AuthenticodeVerifier.Verify(
                fullPath,
                allowNetworkRevocationRetrieval);
            if (!trust.IsTrusted)
            {
                return new AuthenticodePublisherVerificationResult(
                    AuthenticodePublisherStatus.SignatureNotTrusted,
                    hashBefore,
                    trust,
                    null,
                    null,
                    trust.Detail);
            }

            AuthenticodeSignerIdentityResult identity = AuthenticodeSignerIdentityReader.Read(fullPath);
            if (!identity.Succeeded || identity.Evidence is null)
            {
                return new AuthenticodePublisherVerificationResult(
                    AuthenticodePublisherStatus.SignerIdentityUnavailable,
                    hashBefore,
                    trust,
                    null,
                    null,
                    identity.Detail);
            }

            string hashAfter = await FileHashing.Sha256Async(fullPath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(hashBefore, hashAfter, StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticodePublisherVerificationResult(
                    AuthenticodePublisherStatus.FileChangedDuringVerification,
                    null,
                    trust,
                    identity.Evidence,
                    null,
                    "The file changed while its signature and publisher identity were being checked.");
            }

            PublisherPolicyResult evaluation = policy.Evaluate(identity.Evidence);
            return new AuthenticodePublisherVerificationResult(
                evaluation.IsApproved
                    ? AuthenticodePublisherStatus.Approved
                    : AuthenticodePublisherStatus.PublisherNotApproved,
                hashAfter,
                trust,
                identity.Evidence,
                evaluation,
                evaluation.Detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            CryptographicException or
            ArgumentException)
        {
            return new AuthenticodePublisherVerificationResult(
                AuthenticodePublisherStatus.Error,
                null,
                null,
                null,
                null,
                $"Publisher verification could not complete: {exception.GetType().Name}.");
        }
    }
}
