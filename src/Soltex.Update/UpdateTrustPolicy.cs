using System.Security.Cryptography;
using Soltex.Security;

namespace Soltex.Update;

internal sealed class ValidatedUpdateTrustPolicy
{
    internal ValidatedUpdateTrustPolicy(
        UpdateTrustPolicy policy,
        IReadOnlyDictionary<string, UpdateRsaTrustKey> metadataKeys,
        IReadOnlyDictionary<string, UpdateRsaTrustKey> releaseKeys,
        IReadOnlyDictionary<string, IReadOnlySet<string>> activeTlsPins,
        DateTimeOffset evaluationTimeUtc,
        IReadOnlyList<UpdatePublisherPin> eligiblePublisherPins,
        PublisherPolicy publisherPolicy)
    {
        Policy = policy;
        MetadataKeys = metadataKeys;
        ReleaseKeys = releaseKeys;
        ActiveTlsPins = activeTlsPins;
        EvaluationTimeUtc = evaluationTimeUtc;
        EligiblePublisherPins = eligiblePublisherPins;
        PublisherPolicy = publisherPolicy;
    }

    internal UpdateTrustPolicy Policy { get; }
    internal IReadOnlyDictionary<string, UpdateRsaTrustKey> MetadataKeys { get; }
    internal IReadOnlyDictionary<string, UpdateRsaTrustKey> ReleaseKeys { get; }
    internal IReadOnlyDictionary<string, IReadOnlySet<string>> ActiveTlsPins { get; }
    internal DateTimeOffset EvaluationTimeUtc { get; }
    internal IReadOnlyList<UpdatePublisherPin> EligiblePublisherPins { get; }
    internal PublisherPolicy PublisherPolicy { get; }
}

internal static class UpdateTrustPolicyValidator
{
    private const int SupportedSchemaVersion = 1;
    private const int MaximumKeys = 32;
    private const int MaximumPins = 128;
    private const int MaximumPublishers = 32;

    internal static ValidatedUpdateTrustPolicy Validate(
        UpdateTrustPolicy policy,
        DateTimeOffset nowUtc,
        bool requireCurrentlyValid = true)
    {
        ArgumentNullException.ThrowIfNull(policy);
        RequireUtc(nowUtc, nameof(nowUtc));
        if (policy.SchemaVersion != SupportedSchemaVersion || policy.Sequence <= 0)
        {
            throw new InvalidDataException("The update trust-policy schema or sequence is invalid.");
        }

        RequireUtc(policy.ValidFromUtc, nameof(policy.ValidFromUtc));
        RequireUtc(policy.ValidUntilUtc, nameof(policy.ValidUntilUtc));
        if (policy.ValidUntilUtc <= policy.ValidFromUtc)
        {
            throw new InvalidDataException("The update trust-policy validity interval is invalid.");
        }

        if (requireCurrentlyValid &&
            (nowUtc < policy.ValidFromUtc || nowUtc > policy.ValidUntilUtc))
        {
            throw new InvalidDataException("The update trust policy is not currently valid.");
        }

        DateTimeOffset effectiveTime = requireCurrentlyValid || policy.ValidFromUtc <= nowUtc
            ? nowUtc
            : policy.ValidFromUtc;

        if (policy.MetadataKeys is null ||
            policy.ReleaseKeys is null ||
            policy.TlsPins is null ||
            policy.Publishers is null ||
            policy.MetadataKeys.Count is < 1 or > MaximumKeys ||
            policy.ReleaseKeys.Count is < 1 or > MaximumKeys ||
            policy.TlsPins.Count is < 1 or > MaximumPins ||
            policy.Publishers.Count is < 1 or > MaximumPublishers)
        {
            throw new InvalidDataException("The update trust policy has invalid collection bounds.");
        }

        Dictionary<string, UpdateRsaTrustKey> metadata = ValidateKeys(
            policy.MetadataKeys,
            "metadata");
        Dictionary<string, UpdateRsaTrustKey> release = ValidateKeys(
            policy.ReleaseKeys,
            "release");
        if (metadata.Keys.Intersect(release.Keys, StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException(
                "Metadata and release keys must not reuse the same key ID.");
        }

        int eligibleMetadata = metadata.Values.Count(key =>
            IsEligibleForVerification(key, effectiveTime));
        if (policy.DescriptorSignatureQuorum is < 1 ||
            policy.DescriptorSignatureQuorum > eligibleMetadata)
        {
            throw new InvalidDataException(
                "The descriptor signature quorum exceeds eligible metadata keys.");
        }

        if (!release.Values.Any(key => IsEligibleForVerification(key, effectiveTime)))
        {
            throw new InvalidDataException(
                "The update trust policy has no eligible release key.");
        }

        Dictionary<string, HashSet<string>> pins = new(StringComparer.Ordinal);
        HashSet<string> uniquePins = new(StringComparer.Ordinal);
        foreach (UpdateTlsPin? pin in policy.TlsPins)
        {
            if (pin is null || !UpdateText.IsSha256(pin.SpkiSha256))
            {
                throw new InvalidDataException("The update trust policy contains an invalid TLS pin.");
            }

            string origin = UpdateUri.NormalizeOrigin(pin.Origin);
            RequireUtc(pin.NotBeforeUtc, nameof(pin.NotBeforeUtc));
            RequireUtc(pin.NotAfterUtc, nameof(pin.NotAfterUtc));
            if (pin.NotAfterUtc <= pin.NotBeforeUtc)
            {
                throw new InvalidDataException("A TLS pin validity interval is invalid.");
            }

            ValidateRetirement(
                pin.Status,
                pin.RetireAfterUtc,
                pin.NotBeforeUtc,
                pin.NotAfterUtc);
            string normalizedHash = pin.SpkiSha256.ToUpperInvariant();
            string identity = origin + "|" + normalizedHash;
            if (!uniquePins.Add(identity))
            {
                throw new InvalidDataException("The update trust policy contains a duplicate TLS pin.");
            }

            if (IsEligibleForVerification(pin, effectiveTime))
            {
                if (!pins.TryGetValue(origin, out HashSet<string>? originPins))
                {
                    originPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    pins.Add(origin, originPins);
                }

                originPins.Add(normalizedHash);
            }
        }

        if (pins.Count == 0 || pins.Values.Any(set => set.Count == 0))
        {
            throw new InvalidDataException("The update trust policy has no eligible TLS pins.");
        }

        List<UpdatePublisherPin> eligiblePublishers = ValidatePublisherPins(
            policy.Publishers,
            effectiveTime);
        PublisherPolicy publisherPolicy = new(eligiblePublishers.Select(pin => pin.Identity));
        return new ValidatedUpdateTrustPolicy(
            policy,
            metadata,
            release,
            pins.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value,
                StringComparer.Ordinal),
            effectiveTime,
            eligiblePublishers.AsReadOnly(),
            publisherPolicy);
    }

    internal static bool IsEligibleForVerification(UpdateRsaTrustKey key, DateTimeOffset nowUtc) =>
        (key.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        nowUtc >= key.NotBeforeUtc &&
        nowUtc <= key.NotAfterUtc &&
        (key.Status != UpdateTrustStatus.Retiring ||
         key.RetireAfterUtc is null ||
         nowUtc <= key.RetireAfterUtc.Value);

    internal static bool IsEligibleForVerification(UpdateTlsPin pin, DateTimeOffset nowUtc) =>
        (pin.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        nowUtc >= pin.NotBeforeUtc &&
        nowUtc <= pin.NotAfterUtc &&
        (pin.Status != UpdateTrustStatus.Retiring ||
         pin.RetireAfterUtc is null ||
         nowUtc <= pin.RetireAfterUtc.Value);

    internal static bool IsEligibleForVerification(UpdatePublisherPin pin, DateTimeOffset nowUtc) =>
        (pin.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        nowUtc >= pin.NotBeforeUtc &&
        nowUtc <= pin.NotAfterUtc &&
        (pin.Status != UpdateTrustStatus.Retiring ||
         pin.RetireAfterUtc is null ||
         nowUtc <= pin.RetireAfterUtc.Value);

    internal static string PublicKeyIdentity(UpdateRsaTrustKey key)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(key.PublicKeyPem);
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
    }

    private static Dictionary<string, UpdateRsaTrustKey> ValidateKeys(
        IEnumerable<UpdateRsaTrustKey> keys,
        string description)
    {
        Dictionary<string, UpdateRsaTrustKey> result = new(StringComparer.Ordinal);
        foreach (UpdateRsaTrustKey? key in keys)
        {
            if (key is null || !IsKeyId(key.KeyId))
            {
                throw new InvalidDataException($"The {description} key collection contains an invalid ID.");
            }

            RequireUtc(key.NotBeforeUtc, nameof(key.NotBeforeUtc));
            RequireUtc(key.NotAfterUtc, nameof(key.NotAfterUtc));
            if (key.NotAfterUtc <= key.NotBeforeUtc)
            {
                throw new InvalidDataException($"A {description} key validity interval is invalid.");
            }

            ValidateRetirement(
                key.Status,
                key.RetireAfterUtc,
                key.NotBeforeUtc,
                key.NotAfterUtc);
            try
            {
                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(key.PublicKeyPem);
                if (rsa.KeySize < 2048)
                {
                    throw new InvalidDataException($"A {description} RSA key is smaller than 2048 bits.");
                }
            }
            catch (CryptographicException exception)
            {
                throw new InvalidDataException($"A {description} public key is invalid.", exception);
            }

            if (!result.TryAdd(key.KeyId, key))
            {
                throw new InvalidDataException($"The {description} key collection contains duplicate IDs.");
            }
        }

        return result;
    }

    private static List<UpdatePublisherPin> ValidatePublisherPins(
        IEnumerable<UpdatePublisherPin> pins,
        DateTimeOffset nowUtc)
    {
        HashSet<(string Subject, string Key)> unique = new();
        List<UpdatePublisherPin> eligible = [];
        foreach (UpdatePublisherPin? pin in pins)
        {
            if (pin?.Identity is null)
            {
                throw new InvalidDataException(
                    "The update trust policy contains an invalid publisher pin.");
            }

            RequireUtc(pin.NotBeforeUtc, nameof(pin.NotBeforeUtc));
            RequireUtc(pin.NotAfterUtc, nameof(pin.NotAfterUtc));
            if (pin.NotAfterUtc <= pin.NotBeforeUtc)
            {
                throw new InvalidDataException(
                    "A publisher-pin validity interval is invalid.");
            }

            ValidateRetirement(
                pin.Status,
                pin.RetireAfterUtc,
                pin.NotBeforeUtc,
                pin.NotAfterUtc);
            if (!unique.Add((
                    pin.Identity.SubjectNameSha256,
                    pin.Identity.SubjectPublicKeyInfoSha256)))
            {
                throw new InvalidDataException(
                    "The update trust policy contains a duplicate publisher pin.");
            }

            if (IsEligibleForVerification(pin, nowUtc))
            {
                eligible.Add(pin);
            }
        }

        if (eligible.Count == 0)
        {
            throw new InvalidDataException(
                "The update trust policy has no eligible publisher pins.");
        }

        return eligible;
    }

    private static bool IsKeyId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 3 and <= 64 &&
        (value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        (value[^1] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');

    private static void ValidateRetirement(
        UpdateTrustStatus status,
        DateTimeOffset? retireAfterUtc,
        DateTimeOffset notBeforeUtc,
        DateTimeOffset notAfterUtc)
    {
        if (retireAfterUtc is not null)
        {
            RequireUtc(retireAfterUtc.Value, nameof(retireAfterUtc));
            if (retireAfterUtc < notBeforeUtc || retireAfterUtc > notAfterUtc)
            {
                throw new InvalidDataException(
                    "A trust-item retirement time is outside its validity interval.");
            }
        }

        if (status is UpdateTrustStatus.Retiring or UpdateTrustStatus.Retired)
        {
            if (retireAfterUtc is null)
            {
                throw new InvalidDataException(
                    "A retiring or retired trust item requires a retirement time.");
            }
        }
        else if (status == UpdateTrustStatus.Active && retireAfterUtc is not null)
        {
            throw new InvalidDataException(
                "An active trust item cannot carry a retirement time; mark it retiring first.");
        }
    }

    internal static void RequireUtc(DateTimeOffset value, string description)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException($"{description} must be an explicit UTC timestamp.");
        }
    }
}

internal static class UpdateUri
{
    internal static string NormalizeOrigin(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException(
                "An update origin must be an exact credential-free HTTPS authority.");
        }

        return NormalizeOrigin(uri);
    }

    internal static string NormalizeOrigin(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Update network origins must use credential-free HTTPS.");
        }

        UriBuilder builder = new(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    internal static Uri ValidateArtifactUri(string value, string expectedOrigin)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(NormalizeOrigin(uri), expectedOrigin, StringComparison.Ordinal))
        {
            throw new InvalidDataException("An update artifact URI is outside the signed origin.");
        }

        return uri;
    }
}
