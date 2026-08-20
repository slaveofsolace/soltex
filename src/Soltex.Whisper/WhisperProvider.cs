using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Soltex.Whisper;

public enum WhisperProviderLanguageCapability
{
    AutomaticOnly,
    ExplicitSelection,
    AutomaticAndExplicit
}

/// <summary>
/// Content-free provider metadata shown by setup and readiness surfaces. This is a
/// status snapshot, not a credential container and not an authorization decision.
/// </summary>
public sealed class WhisperProviderStatus
{
    public const int MaximumProviderIdCharacters = 64;
    public const int MaximumDisplayNameCharacters = 80;
    public const int MaximumModelIdentityCharacters = 128;
    public const int MaximumPrivacyStatementCharacters = 600;
    public const int MaximumLanguageTags = 64;

    public WhisperProviderStatus(
        string providerId,
        string displayName,
        Uri endpoint,
        string modelIdentity,
        WhisperProviderLanguageCapability languageCapability,
        IEnumerable<string> languageTags,
        bool supportsStreaming,
        string privacyStatement,
        bool credentialAvailable)
    {
        ProviderId = ValidateProviderId(providerId);
        DisplayName = ValidatePrintable(
            displayName,
            MaximumDisplayNameCharacters,
            nameof(displayName));
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "Provider endpoints must be absolute HTTPS addresses without credentials, queries, or fragments.",
                nameof(endpoint));
        }

        Endpoint = endpoint;
        ModelIdentity = ValidatePrintable(
            modelIdentity,
            MaximumModelIdentityCharacters,
            nameof(modelIdentity));
        if (!Enum.IsDefined(languageCapability))
        {
            throw new ArgumentOutOfRangeException(nameof(languageCapability));
        }

        LanguageCapability = languageCapability;
        ArgumentNullException.ThrowIfNull(languageTags);
        string[] acceptedTags = languageTags
            .Select(ValidateLanguageTag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (acceptedTags.Length > MaximumLanguageTags)
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageTags),
                $"A provider cannot expose more than {MaximumLanguageTags} language tags.");
        }

        LanguageTags = Array.AsReadOnly(acceptedTags);
        SupportsStreaming = supportsStreaming;
        PrivacyStatement = ValidatePrintable(
            privacyStatement,
            MaximumPrivacyStatementCharacters,
            nameof(privacyStatement));
        CredentialAvailable = credentialAvailable;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public Uri Endpoint { get; }

    public string ModelIdentity { get; }

    public WhisperProviderLanguageCapability LanguageCapability { get; }

    public ReadOnlyCollection<string> LanguageTags { get; }

    public bool SupportsStreaming { get; }

    public string PrivacyStatement { get; }

    public bool CredentialAvailable { get; }

    internal static string ValidateProviderId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        string normalized = providerId.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumProviderIdCharacters ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Provider ids must contain only ASCII letters, digits, dots, dashes, or underscores.",
                nameof(providerId));
        }

        return normalized;
    }

    private static string ValidatePrintable(string value, int maximum, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximum || normalized.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must contain 1 to {maximum} printable characters.");
        }

        return normalized;
    }

    private static string ValidateLanguageTag(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        string normalized = languageTag.Trim();
        if (normalized.Length is < 2 or > 35 ||
            normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "Provider language tags must be bounded BCP-47-like identifiers.",
                nameof(languageTag));
        }

        return normalized;
    }
}

/// <summary>
/// Owns one ephemeral clear credential. Any ReadOnlyMemory obtained from this lease
/// observes zeroes after disposal because it references the same owned array.
/// </summary>
public sealed class WhisperCredentialLease : IDisposable
{
    private byte[]? _credential;

    public WhisperCredentialLease(ReadOnlySpan<byte> credential)
    {
        if (credential.Length is < 1 or > 8_192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(credential),
                "Provider credentials must contain 1 to 8192 bytes.");
        }

        _credential = credential.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes =>
        _credential ?? throw new ObjectDisposedException(nameof(WhisperCredentialLease));

    public bool IsDisposed => _credential is null;

    public void Dispose()
    {
        byte[]? credential = Interlocked.Exchange(ref _credential, null);
        if (credential is not null)
        {
            CryptographicOperations.ZeroMemory(credential);
        }

        GC.SuppressFinalize(this);
    }
}
