using System.Security.Cryptography;
using WaveSlate.Security;

namespace WaveSlate.Update;

public static class UpdateDescriptorVerifier
{
    private const int MaximumDescriptorBytes = 1024 * 1024;
    private const int MaximumSignatures = 32;
    private const int MaximumSignatureBytes = 16 * 1024;
    private const int MaximumArtifactCount = 3;
    private static readonly HashSet<string> RequiredArtifacts = new(
        ["manifest", "signature", "package"],
        StringComparer.Ordinal);

    public static UpdateDescriptorVerificationResult Verify(
        ReadOnlySpan<byte> descriptorBytes,
        IReadOnlyList<UpdateDetachedSignature> signatures,
        UpdateTrustPolicy trustPolicy,
        DateTimeOffset nowUtc)
    {
        UpdateTrustPolicyValidator.RequireUtc(nowUtc, nameof(nowUtc));
        if (descriptorBytes.Length is < 1 or > MaximumDescriptorBytes)
        {
            return Failure(
                UpdateDescriptorStatus.InvalidDescriptor,
                "The update descriptor size is outside the accepted bounds.");
        }

        ValidatedUpdateTrustPolicy policy;
        try
        {
            policy = UpdateTrustPolicyValidator.Validate(trustPolicy, nowUtc);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            CryptographicException or
            ArgumentException)
        {
            return Failure(
                UpdateDescriptorStatus.InvalidPolicy,
                UpdateText.Sanitize(exception.Message));
        }

        UpdateAcquisitionDescriptor descriptor;
        try
        {
            descriptor = StrictJson.Deserialize<UpdateAcquisitionDescriptor>(descriptorBytes);
        }
        catch (InvalidDataException)
        {
            return Failure(
                UpdateDescriptorStatus.InvalidJson,
                "The update descriptor is not strict duplicate-free JSON.");
        }

        DescriptorValidation validated;
        try
        {
            validated = ValidateDescriptor(descriptor, policy, nowUtc);
        }
        catch (DescriptorValidationException exception)
        {
            return Failure(exception.Status, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                UpdateDescriptorStatus.InvalidDescriptor,
                UpdateText.Sanitize(exception.Message));
        }

        SignatureVerificationResult signatureResult = VerifySignatures(
            descriptorBytes,
            signatures,
            policy.MetadataKeys,
            policy.Policy.DescriptorSignatureQuorum,
            nowUtc,
            excludedKeyIds: null);
        if (!signatureResult.Succeeded)
        {
            return Failure(signatureResult.Status, signatureResult.Detail);
        }

        string descriptorHash = Convert.ToHexString(SHA256.HashData(descriptorBytes));
        return new UpdateDescriptorVerificationResult(
            UpdateDescriptorStatus.Verified,
            new VerifiedUpdateDescriptor(
                descriptor,
                descriptorHash,
                signatureResult.ValidKeyIds,
                validated.ArtifactUris,
                validated.AllowedOrigins),
            "The signed update acquisition descriptor is verified.");
    }

    internal static SignatureVerificationResult VerifySignatures(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<UpdateDetachedSignature> signatures,
        IReadOnlyDictionary<string, UpdateRsaTrustKey> keys,
        int quorum,
        DateTimeOffset nowUtc,
        IReadOnlySet<string>? excludedKeyIds)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        if (signatures.Count is < 1 or > MaximumSignatures)
        {
            return SignatureVerificationResult.Fail(
                UpdateDescriptorStatus.InsufficientSignatures,
                "The detached signature set is outside the accepted bounds.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> valid = [];
        foreach (UpdateDetachedSignature? detached in signatures)
        {
            if (detached is null ||
                !seen.Add(detached.KeyId) ||
                !keys.TryGetValue(detached.KeyId, out UpdateRsaTrustKey key) ||
                excludedKeyIds?.Contains(detached.KeyId) == true ||
                !UpdateTrustPolicyValidator.IsEligibleForVerification(key, nowUtc))
            {
                return SignatureVerificationResult.Fail(
                    UpdateDescriptorStatus.SignatureInvalid,
                    "The detached signature set contains an unknown, duplicate, excluded, or ineligible key.");
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(detached.SignatureBase64);
            }
            catch (FormatException)
            {
                return SignatureVerificationResult.Fail(
                    UpdateDescriptorStatus.SignatureInvalid,
                    "A detached signature is not valid base64.");
            }

            if (signature.Length is < 1 or > MaximumSignatureBytes)
            {
                return SignatureVerificationResult.Fail(
                    UpdateDescriptorStatus.SignatureInvalid,
                    "A detached signature size is outside the accepted bounds.");
            }

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(key.PublicKeyPem);
            if (!rsa.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss))
            {
                return SignatureVerificationResult.Fail(
                    UpdateDescriptorStatus.SignatureInvalid,
                    "A detached update signature is invalid.");
            }

            valid.Add(detached.KeyId);
        }

        if (valid.Count < quorum)
        {
            return SignatureVerificationResult.Fail(
                UpdateDescriptorStatus.InsufficientSignatures,
                "The detached update signature quorum was not met.");
        }

        valid.Sort(StringComparer.Ordinal);
        return new SignatureVerificationResult(
            true,
            UpdateDescriptorStatus.Verified,
            valid.AsReadOnly(),
            "The detached signature quorum is valid.");
    }

    private static DescriptorValidation ValidateDescriptor(
        UpdateAcquisitionDescriptor descriptor,
        ValidatedUpdateTrustPolicy policy,
        DateTimeOffset nowUtc)
    {
        if (descriptor.SchemaVersion != 1 ||
            !string.Equals(descriptor.Product, "Soltex", StringComparison.Ordinal) ||
            descriptor.Sequence <= 0 ||
            string.IsNullOrWhiteSpace(descriptor.DescriptorId) ||
            descriptor.DescriptorId.Length > 128 ||
            !string.Equals(
                descriptor.DescriptorId,
                descriptor.DescriptorId.Trim(),
                StringComparison.Ordinal) ||
            descriptor.DescriptorId.Any(char.IsControl) ||
            !IsChannel(descriptor.Channel) ||
            string.IsNullOrWhiteSpace(descriptor.Version) ||
            descriptor.Version.Length > 64 ||
            !string.Equals(
                descriptor.Version,
                descriptor.Version.Trim(),
                StringComparison.Ordinal) ||
            descriptor.Version.Any(char.IsControl))
        {
            throw new InvalidDataException("The update descriptor header is invalid.");
        }

        UpdateTrustPolicyValidator.RequireUtc(descriptor.IssuedAtUtc, nameof(descriptor.IssuedAtUtc));
        UpdateTrustPolicyValidator.RequireUtc(descriptor.ExpiresAtUtc, nameof(descriptor.ExpiresAtUtc));
        if (descriptor.ExpiresAtUtc <= descriptor.IssuedAtUtc ||
            descriptor.ExpiresAtUtc - descriptor.IssuedAtUtc > TimeSpan.FromDays(7))
        {
            throw new InvalidDataException("The update descriptor validity interval is invalid.");
        }

        if (nowUtc < descriptor.IssuedAtUtc)
        {
            throw new DescriptorValidationException(
                UpdateDescriptorStatus.NotYetValid,
                "The update descriptor is not yet valid.");
        }

        if (nowUtc > descriptor.ExpiresAtUtc)
        {
            throw new DescriptorValidationException(
                UpdateDescriptorStatus.Expired,
                "The update descriptor has expired.");
        }

        if (!policy.ReleaseKeys.TryGetValue(descriptor.ReleaseKeyId, out UpdateRsaTrustKey releaseKey) ||
            !UpdateTrustPolicyValidator.IsEligibleForVerification(releaseKey, nowUtc))
        {
            throw new InvalidDataException("The update descriptor selects an unavailable release key.");
        }

        string origin = UpdateUri.NormalizeOrigin(descriptor.Origin);
        if (!policy.ActiveTlsPins.ContainsKey(origin))
        {
            throw new InvalidDataException("The signed update origin has no eligible TLS pin.");
        }

        if (descriptor.AllowedRedirectOrigins is null ||
            descriptor.AllowedRedirectOrigins.Count > 8 ||
            descriptor.MaximumRedirects is < 0 or > 5 ||
            descriptor.HeaderTimeoutSeconds is < 1 or > 60 ||
            descriptor.ReadTimeoutSeconds is < 1 or > 60 ||
            descriptor.OverallTimeoutSeconds is < 1 or > 600 ||
            descriptor.OverallTimeoutSeconds < descriptor.HeaderTimeoutSeconds ||
            descriptor.OverallTimeoutSeconds < descriptor.ReadTimeoutSeconds)
        {
            throw new InvalidDataException("The update descriptor redirect or timeout bounds are invalid.");
        }

        HashSet<string> allowedOrigins = new(StringComparer.Ordinal) { origin };
        foreach (string redirectOrigin in descriptor.AllowedRedirectOrigins)
        {
            string normalized = UpdateUri.NormalizeOrigin(redirectOrigin);
            if (!policy.ActiveTlsPins.ContainsKey(normalized) || !allowedOrigins.Add(normalized))
            {
                throw new InvalidDataException("A redirect origin is duplicate or lacks an eligible TLS pin.");
            }
        }

        if (descriptor.Artifacts is null || descriptor.Artifacts.Count != MaximumArtifactCount)
        {
            throw new InvalidDataException("The update descriptor must contain exactly three artifacts.");
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        Dictionary<string, Uri> artifactUris = new(StringComparer.Ordinal);
        foreach (UpdateArtifactDescriptor? artifact in descriptor.Artifacts)
        {
            if (artifact is null ||
                !RequiredArtifacts.Contains(artifact.Name) ||
                !names.Add(artifact.Name) ||
                artifact.Length <= 0 ||
                !UpdateText.IsSha256(artifact.Sha256))
            {
                throw new InvalidDataException("An update artifact descriptor is invalid or duplicate.");
            }

            long maximum = artifact.Name switch
            {
                "manifest" => 4L * 1024 * 1024,
                "signature" => 16L * 1024,
                "package" => 1024L * 1024 * 1024,
                _ => 0
            };
            if (artifact.Length > maximum)
            {
                throw new InvalidDataException("An update artifact exceeds its signed maximum size.");
            }

            artifactUris.Add(artifact.Name, UpdateUri.ValidateArtifactUri(artifact.Uri, origin));
        }

        if (!names.SetEquals(RequiredArtifacts))
        {
            throw new InvalidDataException("The signed update artifact set is incomplete.");
        }

        return new DescriptorValidation(
            artifactUris,
            allowedOrigins);
    }

    private static bool IsChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) &&
        channel.Length <= 32 &&
        (channel[0] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        (channel[^1] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        channel.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');

    private static UpdateDescriptorVerificationResult Failure(
        UpdateDescriptorStatus status,
        string detail) =>
        new(status, null, UpdateText.Sanitize(detail));

    private sealed class DescriptorValidationException(
        UpdateDescriptorStatus status,
        string message) : InvalidDataException(message)
    {
        internal UpdateDescriptorStatus Status { get; } = status;
    }

    private sealed record DescriptorValidation(
        IReadOnlyDictionary<string, Uri> ArtifactUris,
        IReadOnlySet<string> AllowedOrigins);
}

internal sealed record SignatureVerificationResult(
    bool Succeeded,
    UpdateDescriptorStatus Status,
    IReadOnlyList<string> ValidKeyIds,
    string Detail)
{
    internal static SignatureVerificationResult Fail(
        UpdateDescriptorStatus status,
        string detail) =>
        new(false, status, [], UpdateText.Sanitize(detail));
}
