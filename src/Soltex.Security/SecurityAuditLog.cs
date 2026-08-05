using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Soltex.Security;

public sealed class SecurityAuditLog : IDisposable
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly AuthenticatedJsonStore _stateStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal SecurityAuditLog(string path, AuthenticatedJsonStore stateStore)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _stateStore = stateStore;
    }

    public async Task<SecurityAuditEvent> AppendAsync(
        string eventType,
        SecurityEventSeverity severity,
        string message,
        string? path = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RotateIfNeeded();
            byte[] key = await _stateStore.GetKeyCopyAsync(cancellationToken).ConfigureAwait(false);
            string previousHash = await ReadLastHashAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            Guid id = Guid.NewGuid();
            string? pathHash = string.IsNullOrWhiteSpace(path)
                ? null
                : Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
            string canonical = string.Join(
                "\n",
                id.ToString("D"),
                timestamp.ToString("O"),
                eventType,
                severity,
                Sanitize(message, 500),
                pathHash ?? string.Empty,
                Sanitize(detail ?? string.Empty, 1_000),
                previousHash);
            string entryHash = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
            SecurityAuditEvent entry = new(
                id,
                timestamp,
                eventType,
                severity,
                Sanitize(message, 500),
                pathHash,
                Sanitize(detail ?? string.Empty, 1_000),
                previousHash,
                entryHash);

            string json = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(_path, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(key);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> VerifyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(_path))
        {
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] key = await _stateStore.GetKeyCopyAsync(cancellationToken).ConfigureAwait(false);
            string previousHash = string.Empty;
            foreach (string line in File.ReadLines(_path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                SecurityAuditEvent? entry = JsonSerializer.Deserialize<SecurityAuditEvent>(line, JsonOptions);
                if (entry is null || !string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
                {
                    return false;
                }

                string canonical = string.Join(
                    "\n",
                    entry.Id.ToString("D"),
                    entry.TimestampUtc.ToString("O"),
                    entry.EventType,
                    entry.Severity,
                    entry.Message,
                    entry.PathHash ?? string.Empty,
                    entry.Detail ?? string.Empty,
                    entry.PreviousHash);
                string actual = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual),
                    Convert.FromHexString(entry.EntryHash)))
                {
                    return false;
                }

                previousHash = entry.EntryHash;
            }

            CryptographicOperations.ZeroMemory(key);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or IOException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ReadLastHashAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return string.Empty;
        }

        string? last = null;
        foreach (string line in File.ReadLines(_path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(line))
            {
                last = line;
            }
        }

        if (last is null)
        {
            return string.Empty;
        }

        return JsonSerializer.Deserialize<SecurityAuditEvent>(last, JsonOptions)?.EntryHash ?? string.Empty;
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaximumLogBytes)
        {
            return;
        }

        string archive = _path + ".1";
        File.Move(_path, archive, overwrite: true);
    }

    private static string Sanitize(string value, int maximum)
    {
        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= maximum ? sanitized : sanitized[..maximum];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
