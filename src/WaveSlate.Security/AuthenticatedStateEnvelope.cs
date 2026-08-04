using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaveSlate.Security;

internal enum AuthenticatedEnvelopeKind
{
    Missing,
    NotEnvelope,
    Invalid,
    Valid
}

internal sealed record AuthenticatedEnvelopeCandidate<T>(
    AuthenticatedEnvelopeKind Kind,
    ulong Generation,
    string? PayloadSha256,
    byte[]? RawEnvelope,
    T? Value)
{
    internal bool Valid => Kind == AuthenticatedEnvelopeKind.Valid;

    internal static AuthenticatedEnvelopeCandidate<T> Missing() =>
        new(AuthenticatedEnvelopeKind.Missing, 0, null, null, default);

    internal static AuthenticatedEnvelopeCandidate<T> NotEnvelope() =>
        new(AuthenticatedEnvelopeKind.NotEnvelope, 0, null, null, default);

    internal static AuthenticatedEnvelopeCandidate<T> Invalid() =>
        new(AuthenticatedEnvelopeKind.Invalid, 0, null, null, default);
}

internal static class AuthenticatedStateEnvelope
{
    private const int Version = 1;
    private const int HeaderLength = 24;
    private const int MacLength = 32;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SLTXST01");

    internal const int MaximumPayloadBytes = 4 * 1024 * 1024;
    internal const int MaximumEnvelopeBytes = HeaderLength + MaximumPayloadBytes + MacLength;
    internal const int LegacyMacLength = MacLength;

    internal static bool HasMagic(ReadOnlySpan<byte> raw) =>
        raw.Length >= Magic.Length && raw[..Magic.Length].SequenceEqual(Magic);

    internal static byte[] Build(
        byte[] domain,
        ulong generation,
        byte[] payload,
        byte[] key)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(key);
        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        if (payload.Length is < 1 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "The authenticated-state payload size is outside the accepted bounds.");
        }

        byte[] raw = new byte[checked(HeaderLength + payload.Length + MacLength)];
        Magic.CopyTo(raw, 0);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(8, 4), Version);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(12, 8), generation);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(20, 4), payload.Length);
        payload.CopyTo(raw.AsSpan(HeaderLength));
        using IncrementalHash hmac = IncrementalHash.CreateHMAC(
            HashAlgorithmName.SHA256,
            key);
        hmac.AppendData(domain);
        hmac.AppendData(raw.AsSpan(0, raw.Length - MacLength));
        hmac.GetHashAndReset().CopyTo(raw, raw.Length - MacLength);
        return raw;
    }

    internal static AuthenticatedEnvelopeCandidate<T> Parse<T>(
        byte[] raw,
        byte[] domain,
        byte[] key,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(options);
        if (raw.Length < HeaderLength + MacLength ||
            !HasMagic(raw) ||
            BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8, 4)) != Version)
        {
            return AuthenticatedEnvelopeCandidate<T>.Invalid();
        }

        ulong generation = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(12, 8));
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(20, 4));
        if (generation == 0 ||
            payloadLength is < 1 or > MaximumPayloadBytes ||
            raw.Length != HeaderLength + payloadLength + MacLength)
        {
            return AuthenticatedEnvelopeCandidate<T>.Invalid();
        }

        using IncrementalHash hmac = IncrementalHash.CreateHMAC(
            HashAlgorithmName.SHA256,
            key);
        hmac.AppendData(domain);
        hmac.AppendData(raw.AsSpan(0, raw.Length - MacLength));
        byte[] actualMac = hmac.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(
                raw.AsSpan(raw.Length - MacLength),
                actualMac))
        {
            return AuthenticatedEnvelopeCandidate<T>.Invalid();
        }

        byte[] payload = raw.AsSpan(HeaderLength, payloadLength).ToArray();
        try
        {
            T value = StrictJson.Deserialize<T>(payload, options);
            return new AuthenticatedEnvelopeCandidate<T>(
                AuthenticatedEnvelopeKind.Valid,
                generation,
                Convert.ToHexString(SHA256.HashData(payload)),
                raw,
                value);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return AuthenticatedEnvelopeCandidate<T>.Invalid();
        }
    }
}
