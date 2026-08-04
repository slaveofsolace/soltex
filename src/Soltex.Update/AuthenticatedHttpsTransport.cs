using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;

namespace Soltex.Update;

public interface IAuthenticatedHttpsTransport
{
    Task<AuthenticatedHttpsResponse> OpenAsync(
        Uri uri,
        IReadOnlySet<string> acceptedTlsSpkiSha256,
        TimeSpan headerTimeout,
        CancellationToken cancellationToken);
}

public sealed class AuthenticatedHttpsResponse : IAsyncDisposable, IDisposable
{
    private readonly IDisposable[] _owned;
    private bool _disposed;

    internal AuthenticatedHttpsResponse(
        HttpStatusCode statusCode,
        Uri requestUri,
        Uri effectiveUri,
        Uri? redirectLocation,
        long? contentLength,
        string tlsSpkiSha256,
        Stream content,
        params IDisposable[] owned)
    {
        StatusCode = statusCode;
        RequestUri = requestUri;
        EffectiveUri = effectiveUri;
        RedirectLocation = redirectLocation;
        ContentLength = contentLength;
        TlsSpkiSha256 = tlsSpkiSha256;
        Content = content;
        _owned = owned;
    }

    public HttpStatusCode StatusCode { get; }
    public Uri RequestUri { get; }
    public Uri EffectiveUri { get; }
    public Uri? RedirectLocation { get; }
    public long? ContentLength { get; }
    public string TlsSpkiSha256 { get; }
    public Stream Content { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Content.Dispose();
        foreach (IDisposable owned in _owned)
        {
            owned.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await Content.DisposeAsync().ConfigureAwait(false);
        foreach (IDisposable owned in _owned)
        {
            owned.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public sealed class PinnedHttpsTransport : IAuthenticatedHttpsTransport
{
    public async Task<AuthenticatedHttpsResponse> OpenAsync(
        Uri uri,
        IReadOnlySet<string> acceptedTlsSpkiSha256,
        TimeSpan headerTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(acceptedTlsSpkiSha256);
        if (acceptedTlsSpkiSha256.Count == 0)
        {
            throw new InvalidDataException("No TLS pins are available for the update origin.");
        }

        if (headerTimeout <= TimeSpan.Zero || headerTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(headerTimeout));
        }

        string expectedOrigin = UpdateUri.NormalizeOrigin(uri);
        string? observedPin = null;
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            CheckCertificateRevocationList = true,
            PreAuthenticate = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 64
        };
        handler.ServerCertificateCustomValidationCallback = (
            request,
            certificate,
            _,
            policyErrors) =>
        {
            if (request.RequestUri is null ||
                certificate is null ||
                policyErrors != SslPolicyErrors.None ||
                !string.Equals(
                    UpdateUri.NormalizeOrigin(request.RequestUri),
                    expectedOrigin,
                    StringComparison.Ordinal))
            {
                return false;
            }

            string pin = Convert.ToHexString(SHA256.HashData(
                certificate.PublicKey.ExportSubjectPublicKeyInfo()));
            bool accepted = acceptedTlsSpkiSha256.Contains(pin);
            if (accepted)
            {
                observedPin = pin;
            }

            return accepted;
        };

        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        HttpRequestMessage request = new(HttpMethod.Get, uri)
        {
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };

        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(headerTimeout);
            HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            Stream content = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(observedPin))
            {
                content.Dispose();
                response.Dispose();
                throw new HttpRequestException(
                    "The HTTPS response did not retain verified TLS pin evidence.");
            }

            Uri effectiveUri = response.RequestMessage?.RequestUri ?? uri;
            return new AuthenticatedHttpsResponse(
                response.StatusCode,
                uri,
                effectiveUri,
                response.Headers.Location,
                response.Content.Headers.ContentLength,
                observedPin,
                content,
                response,
                request,
                client);
        }
        catch
        {
            request.Dispose();
            client.Dispose();
            throw;
        }
    }
}
