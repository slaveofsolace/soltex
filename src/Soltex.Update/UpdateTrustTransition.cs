using System.Security.Cryptography;
using Soltex.Security;

namespace Soltex.Update;

public static class UpdateTrustTransitionEvaluator
{
    private const int MaximumPolicyBytes = 1024 * 1024;

    public static UpdateTrustTransitionDecision Evaluate(
        ReadOnlySpan<byte> currentPolicyBytes,
        ReadOnlySpan<byte> proposedPolicyBytes,
        IReadOnlyList<UpdateDetachedSignature> signatures,
        DateTimeOffset nowUtc)
    {
        long currentSequence = 0;
        long proposedSequence = 0;
        string? proposedHash = null;
        try
        {
            UpdateTrustPolicyValidator.RequireUtc(nowUtc, nameof(nowUtc));
            if (currentPolicyBytes.Length is < 1 or > MaximumPolicyBytes ||
                proposedPolicyBytes.Length is < 1 or > MaximumPolicyBytes)
            {
                throw new InvalidDataException(
                    "A trust-policy document size is outside the accepted bounds.");
            }

            UpdateTrustPolicy current = StrictJson.Deserialize<UpdateTrustPolicy>(currentPolicyBytes);
            UpdateTrustPolicy proposed = StrictJson.Deserialize<UpdateTrustPolicy>(proposedPolicyBytes);
            currentSequence = current.Sequence;
            proposedSequence = proposed.Sequence;
            string currentHash = Convert.ToHexString(SHA256.HashData(currentPolicyBytes));
            proposedHash = Convert.ToHexString(SHA256.HashData(proposedPolicyBytes));
            ValidatedUpdateTrustPolicy currentValidated =
                UpdateTrustPolicyValidator.Validate(current, nowUtc);
            ValidatedUpdateTrustPolicy proposedValidated =
                UpdateTrustPolicyValidator.Validate(
                    proposed,
                    nowUtc,
                    requireCurrentlyValid: false);

            if (proposed.Sequence < current.Sequence)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedRollback,
                    current.Sequence,
                    proposed.Sequence,
                    proposedHash,
                    "The proposed trust-policy sequence is lower than the current sequence.");
            }

            if (proposed.Sequence == current.Sequence)
            {
                return string.Equals(currentHash, proposedHash, StringComparison.Ordinal)
                    ? new UpdateTrustTransitionDecision(
                        UpdateTrustTransitionStatus.AcceptedIdempotent,
                        current.Sequence,
                        proposed.Sequence,
                        proposedHash,
                        [],
                        "The exact signed trust-policy bytes are already current.")
                    : Reject(
                        UpdateTrustTransitionStatus.RejectedEquivocation,
                        current.Sequence,
                        proposed.Sequence,
                        proposedHash,
                        "Different trust-policy bytes reused the current sequence.");
            }

            if (proposed.ValidFromUtc > current.ValidUntilUtc ||
                proposed.ValidUntilUtc <= nowUtc)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedInvalid,
                    current.Sequence,
                    proposed.Sequence,
                    proposedHash,
                    "The proposed trust policy has no valid overlap with the current policy.");
            }

            HashSet<string> excluded = proposed.MetadataKeys
                .Where(key => key.Status is
                    UpdateTrustStatus.Revoked or
                    UpdateTrustStatus.Compromised)
                .Select(key => key.KeyId)
                .ToHashSet(StringComparer.Ordinal);
            SignatureVerificationResult signatureResult =
                UpdateDescriptorVerifier.VerifySignatures(
                    proposedPolicyBytes,
                    signatures,
                    currentValidated.MetadataKeys,
                    current.DescriptorSignatureQuorum,
                    nowUtc,
                    excluded);
            if (!signatureResult.Succeeded)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedInsufficientQuorum,
                    current.Sequence,
                    proposed.Sequence,
                    proposedHash,
                    signatureResult.Detail);
            }

            UpdateTrustTransitionDecision? continuityFailure = ValidateContinuity(
                currentValidated,
                proposedValidated,
                nowUtc,
                proposedHash);
            if (continuityFailure is not null)
            {
                return continuityFailure with
                {
                    AuthorizingKeyIds = signatureResult.ValidKeyIds
                };
            }

            return new UpdateTrustTransitionDecision(
                UpdateTrustTransitionStatus.AcceptedUpgrade,
                current.Sequence,
                proposed.Sequence,
                proposedHash,
                signatureResult.ValidKeyIds,
                "The signed trust-policy rotation preserves quorum, identity, and planned overlap.");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            CryptographicException or
            ArgumentException)
        {
            return new UpdateTrustTransitionDecision(
                UpdateTrustTransitionStatus.RejectedInvalid,
                currentSequence,
                proposedSequence,
                proposedHash,
                [],
                UpdateText.Sanitize(exception.Message));
        }
    }

    private static UpdateTrustTransitionDecision? ValidateContinuity(
        ValidatedUpdateTrustPolicy current,
        ValidatedUpdateTrustPolicy proposed,
        DateTimeOffset nowUtc,
        string proposedHash)
    {
        UpdateTrustTransitionDecision? metadataFailure = ValidateKeySetContinuity(
            current.MetadataKeys,
            proposed.MetadataKeys,
            current,
            proposed,
            proposedHash);
        if (metadataFailure is not null)
        {
            return metadataFailure;
        }

        UpdateTrustTransitionDecision? releaseFailure = ValidateKeySetContinuity(
            current.ReleaseKeys,
            proposed.ReleaseKeys,
            current,
            proposed,
            proposedHash);
        if (releaseFailure is not null)
        {
            return releaseFailure;
        }

        int metadataOverlap = current.MetadataKeys.Values.Count(currentKey =>
            UpdateTrustPolicyValidator.IsEligibleForVerification(currentKey, nowUtc) &&
            proposed.MetadataKeys.TryGetValue(
                currentKey.KeyId,
                out UpdateRsaTrustKey? proposedKey) &&
            SameKey(currentKey, proposedKey) &&
            UpdateTrustPolicyValidator.IsEligibleForVerification(
                proposedKey,
                proposed.EvaluationTimeUtc));
        if (metadataOverlap < proposed.Policy.DescriptorSignatureQuorum)
        {
            return Reject(
                UpdateTrustTransitionStatus.RejectedMetadataOverlap,
                current.Policy.Sequence,
                proposed.Policy.Sequence,
                proposedHash,
                "The proposed metadata trust set lacks quorum-preserving key overlap.");
        }

        bool releaseOverlap = current.ReleaseKeys.Values.Any(currentKey =>
            UpdateTrustPolicyValidator.IsEligibleForVerification(currentKey, nowUtc) &&
            proposed.ReleaseKeys.TryGetValue(
                currentKey.KeyId,
                out UpdateRsaTrustKey? proposedKey) &&
            SameKey(currentKey, proposedKey) &&
            UpdateTrustPolicyValidator.IsEligibleForVerification(
                proposedKey,
                proposed.EvaluationTimeUtc));
        if (!releaseOverlap)
        {
            return Reject(
                UpdateTrustTransitionStatus.RejectedReleaseKeyOverlap,
                current.Policy.Sequence,
                proposed.Policy.Sequence,
                proposedHash,
                "The proposed release-key set has no eligible overlap with the current set.");
        }

        UpdateTrustTransitionDecision? tlsFailure = ValidateTlsPinContinuity(
            current,
            proposed,
            nowUtc,
            proposedHash);
        if (tlsFailure is not null)
        {
            return tlsFailure;
        }

        return ValidatePublisherContinuity(
            current,
            proposed,
            nowUtc,
            proposedHash);
    }

    private static UpdateTrustTransitionDecision? ValidateKeySetContinuity(
        IReadOnlyDictionary<string, UpdateRsaTrustKey> currentKeys,
        IReadOnlyDictionary<string, UpdateRsaTrustKey> proposedKeys,
        ValidatedUpdateTrustPolicy current,
        ValidatedUpdateTrustPolicy proposed,
        string proposedHash)
    {
        foreach ((string keyId, UpdateRsaTrustKey currentKey) in currentKeys)
        {
            if (!proposedKeys.TryGetValue(keyId, out UpdateRsaTrustKey? proposedKey))
            {
                if (RequiresPlannedContinuity(currentKey, proposed.EvaluationTimeUtc))
                {
                    return Reject(
                        UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                        current.Policy.Sequence,
                        proposed.Policy.Sequence,
                        proposedHash,
                        "A current trust key disappeared before its signed retirement time.");
                }

                continue;
            }

            if (!SameKey(currentKey, proposedKey))
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedKeyReplacement,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A trust key ID changed public-key material.");
            }

            if (IsTerminal(currentKey.Status) &&
                proposedKey.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedKeyRevival,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A retired, revoked, or compromised key was revived.");
            }

            if (proposedKey.Status is UpdateTrustStatus.Revoked or UpdateTrustStatus.Compromised)
            {
                continue;
            }

            if (proposedKey.Status == UpdateTrustStatus.Retired &&
                RequiresPlannedContinuity(currentKey, proposed.EvaluationTimeUtc))
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A current trust key was retired before its signed retirement time.");
            }
        }

        return null;
    }

    private static UpdateTrustTransitionDecision? ValidateTlsPinContinuity(
        ValidatedUpdateTrustPolicy current,
        ValidatedUpdateTrustPolicy proposed,
        DateTimeOffset nowUtc,
        string proposedHash)
    {
        foreach (UpdateTlsPin currentPin in current.Policy.TlsPins)
        {
            UpdateTlsPin? proposedPin = proposed.Policy.TlsPins.SingleOrDefault(candidate =>
                SameTlsPin(currentPin, candidate));
            if (proposedPin is null)
            {
                if (RequiresPlannedContinuity(currentPin, proposed.EvaluationTimeUtc))
                {
                    return Reject(
                        UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                        current.Policy.Sequence,
                        proposed.Policy.Sequence,
                        proposedHash,
                        "A current TLS pin disappeared before its signed retirement time.");
                }

                continue;
            }

            if (IsTerminal(currentPin.Status) &&
                proposedPin.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedKeyRevival,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A retired, revoked, or compromised TLS pin was revived.");
            }

            if (proposedPin.Status == UpdateTrustStatus.Retired &&
                RequiresPlannedContinuity(currentPin, proposed.EvaluationTimeUtc))
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A current TLS pin was retired before its signed retirement time.");
            }
        }

        foreach (IGrouping<string, UpdateTlsPin> originPins in current.Policy.TlsPins
                     .Where(pin =>
                         UpdateTrustPolicyValidator.IsEligibleForVerification(pin, nowUtc) &&
                         RequiresPlannedContinuity(pin, proposed.EvaluationTimeUtc))
                     .GroupBy(
                         pin => UpdateUri.NormalizeOrigin(pin.Origin),
                         StringComparer.Ordinal))
        {
            bool overlap = originPins.Any(currentPin => proposed.Policy.TlsPins.Any(proposedPin =>
                SameTlsPin(currentPin, proposedPin) &&
                UpdateTrustPolicyValidator.IsEligibleForVerification(
                    proposedPin,
                    proposed.EvaluationTimeUtc)));
            if (!overlap)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedTlsPinOverlap,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    $"Update origin '{originPins.Key}' lost all eligible overlapping TLS pins.");
            }
        }

        return null;
    }

    private static UpdateTrustTransitionDecision? ValidatePublisherContinuity(
        ValidatedUpdateTrustPolicy current,
        ValidatedUpdateTrustPolicy proposed,
        DateTimeOffset nowUtc,
        string proposedHash)
    {
        foreach (UpdatePublisherPin currentPin in current.Policy.Publishers)
        {
            UpdatePublisherPin? proposedPin = proposed.Policy.Publishers.SingleOrDefault(candidate =>
                SamePublisherPin(currentPin, candidate));
            if (proposedPin is null)
            {
                if (RequiresPlannedContinuity(currentPin, proposed.EvaluationTimeUtc))
                {
                    return Reject(
                        UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                        current.Policy.Sequence,
                        proposed.Policy.Sequence,
                        proposedHash,
                        "A current publisher pin disappeared before its signed retirement time.");
                }

                continue;
            }

            if (IsTerminal(currentPin.Status) &&
                proposedPin.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring)
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedKeyRevival,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A retired, revoked, or compromised publisher pin was revived.");
            }

            if (proposedPin.Status == UpdateTrustStatus.Retired &&
                RequiresPlannedContinuity(currentPin, proposed.EvaluationTimeUtc))
            {
                return Reject(
                    UpdateTrustTransitionStatus.RejectedPrematureRetirement,
                    current.Policy.Sequence,
                    proposed.Policy.Sequence,
                    proposedHash,
                    "A current publisher pin was retired before its signed retirement time.");
            }
        }

        bool overlap = current.Policy.Publishers.Any(currentPin =>
            UpdateTrustPolicyValidator.IsEligibleForVerification(currentPin, nowUtc) &&
            proposed.Policy.Publishers.Any(proposedPin =>
                SamePublisherPin(currentPin, proposedPin) &&
                UpdateTrustPolicyValidator.IsEligibleForVerification(
                    proposedPin,
                    proposed.EvaluationTimeUtc)));
        if (!overlap)
        {
            return Reject(
                UpdateTrustTransitionStatus.RejectedPublisherOverlap,
                current.Policy.Sequence,
                proposed.Policy.Sequence,
                proposedHash,
                "The proposed publisher-pin set has no eligible overlap with the current set.");
        }

        return null;
    }

    private static bool SameKey(UpdateRsaTrustKey left, UpdateRsaTrustKey right) =>
        string.Equals(
            UpdateTrustPolicyValidator.PublicKeyIdentity(left),
            UpdateTrustPolicyValidator.PublicKeyIdentity(right),
            StringComparison.Ordinal);

    private static bool SameTlsPin(UpdateTlsPin left, UpdateTlsPin right) =>
        string.Equals(
            UpdateUri.NormalizeOrigin(left.Origin),
            UpdateUri.NormalizeOrigin(right.Origin),
            StringComparison.Ordinal) &&
        string.Equals(
            left.SpkiSha256,
            right.SpkiSha256,
            StringComparison.OrdinalIgnoreCase);

    private static bool SamePublisherPin(
        UpdatePublisherPin left,
        UpdatePublisherPin right) =>
        string.Equals(
            left.Identity.SubjectNameSha256,
            right.Identity.SubjectNameSha256,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            left.Identity.SubjectPublicKeyInfoSha256,
            right.Identity.SubjectPublicKeyInfoSha256,
            StringComparison.OrdinalIgnoreCase);

    private static bool RequiresPlannedContinuity(
        UpdateRsaTrustKey item,
        DateTimeOffset proposedValidFromUtc) =>
        (item.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        (item.RetireAfterUtc is null || proposedValidFromUtc < item.RetireAfterUtc.Value);

    private static bool RequiresPlannedContinuity(
        UpdateTlsPin item,
        DateTimeOffset proposedValidFromUtc) =>
        (item.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        (item.RetireAfterUtc is null || proposedValidFromUtc < item.RetireAfterUtc.Value);

    private static bool RequiresPlannedContinuity(
        UpdatePublisherPin item,
        DateTimeOffset proposedValidFromUtc) =>
        (item.Status is UpdateTrustStatus.Active or UpdateTrustStatus.Retiring) &&
        (item.RetireAfterUtc is null || proposedValidFromUtc < item.RetireAfterUtc.Value);

    private static bool IsTerminal(UpdateTrustStatus status) =>
        status is UpdateTrustStatus.Retired or
        UpdateTrustStatus.Revoked or
        UpdateTrustStatus.Compromised;

    private static UpdateTrustTransitionDecision Reject(
        UpdateTrustTransitionStatus status,
        long currentSequence,
        long proposedSequence,
        string? proposedHash,
        string detail) =>
        new(
            status,
            currentSequence,
            proposedSequence,
            proposedHash,
            [],
            UpdateText.Sanitize(detail));
}
