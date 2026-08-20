using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Soltex.Security;

/// <summary>
/// Stores bounded history metadata in Soltex's authenticated state envelope while
/// protecting each retained text value with Windows DPAPI for the current user.
/// </summary>
public sealed class AuthenticatedProtectedHistoryStore : IAsyncDisposable
{
    public const int MaximumEntries = 24;
    public const int MaximumTextCharacters = 16_000;

    private const int CurrentVersion = 1;
    private const int MaximumProtectedTextCharacters = 96_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly AuthenticatedJsonStore _store;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public AuthenticatedProtectedHistoryStore(
        string stateRoot,
        string storeName = "whisper-history",
        TimeProvider? timeProvider = null)
    {
        _store = new AuthenticatedJsonStore(stateRoot, storeName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<IReadOnlyList<ProtectedHistoryRecord>> LoadAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRetentionDays(retentionDays);
        ProtectedHistoryDocument document = await _store.LoadAsync(
            static () => new ProtectedHistoryDocument(),
            cancellationToken).ConfigureAwait(false);
        ValidateDocument(document);

        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);
        List<ProtectedHistoryRecord> records = new(document.Entries.Count);
        bool pruned = false;
        foreach (ProtectedHistoryEntry entry in document.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntry(entry);
            if (entry.CreatedAtUtc < cutoff)
            {
                pruned = true;
                continue;
            }

            byte[] protectedBytes;
            try
            {
                protectedBytes = Convert.FromBase64String(entry.ProtectedText);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "Protected history contains invalid encoded data.",
                    exception);
            }

            byte[]? clearBytes = null;
            try
            {
                clearBytes = Dpapi.Unprotect(protectedBytes);
                string text = StrictUtf8.GetString(clearBytes);
                if (text.Length is < 1 or > MaximumTextCharacters)
                {
                    throw new InvalidDataException(
                        "Protected history text is outside the accepted bounds.");
                }

                records.Add(new ProtectedHistoryRecord(
                    entry.CreatedAtUtc,
                    entry.ProcessName,
                    entry.Category,
                    text));
            }
            catch (Exception exception) when (
                exception is CryptographicException or Win32Exception)
            {
                throw new InvalidDataException(
                    "Protected history could not be decrypted for this Windows account.",
                    exception);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Protected history text is not valid UTF-8.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                if (clearBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(clearBytes);
                }
            }
        }

        if (pruned)
        {
            await ReplaceAsync(records, retentionDays, cancellationToken)
                .ConfigureAwait(false);
        }

        return records.AsReadOnly();
    }

    public async ValueTask SaveAsync(
        IReadOnlyCollection<ProtectedHistoryRecord> records,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ProtectedHistoryDocument document = CreateDocument(
            records,
            retentionDays,
            cancellationToken);
        await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes current and backup generations before writing the replacement. Use
    /// this after an entry is deleted or expired so prior Soltex generations do not
    /// retain the removed text.
    /// </summary>
    public async ValueTask ReplaceAsync(
        IReadOnlyCollection<ProtectedHistoryRecord> records,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ProtectedHistoryDocument document = CreateDocument(
            records,
            retentionDays,
            cancellationToken);
        await _store.DeleteStateAsync(cancellationToken).ConfigureAwait(false);
        if (document.Entries.Count > 0)
        {
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _store.DeleteStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private ProtectedHistoryDocument CreateDocument(
        IReadOnlyCollection<ProtectedHistoryRecord> records,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ValidateRetentionDays(retentionDays);
        if (records.Count > MaximumEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(records),
                $"Protected history cannot contain more than {MaximumEntries} entries.");
        }

        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);
        List<ProtectedHistoryEntry> entries = new(records.Count);
        foreach (ProtectedHistoryRecord record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(record);
            ValidateMetadata(record.CreatedAtUtc, record.ProcessName, record.Category);
            if (record.CreatedAtUtc < cutoff)
            {
                continue;
            }

            if (string.IsNullOrEmpty(record.Text) || record.Text.Length > MaximumTextCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(records),
                    $"Protected history text must contain 1 to {MaximumTextCharacters} characters.");
            }

            byte[] clearBytes = StrictUtf8.GetBytes(record.Text);
            byte[]? protectedBytes = null;
            try
            {
                protectedBytes = Dpapi.Protect(clearBytes);
                string encoded = Convert.ToBase64String(protectedBytes);
                if (encoded.Length > MaximumProtectedTextCharacters)
                {
                    throw new InvalidDataException(
                        "Protected history output exceeded its storage bound.");
                }

                entries.Add(new ProtectedHistoryEntry
                {
                    CreatedAtUtc = record.CreatedAtUtc,
                    ProcessName = record.ProcessName,
                    Category = record.Category,
                    ProtectedText = encoded
                });
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
                if (protectedBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
        }

        return new ProtectedHistoryDocument { Entries = entries };
    }

    private static void ValidateDocument(ProtectedHistoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != CurrentVersion || document.Entries is null)
        {
            throw new InvalidDataException("Protected history has an unsupported format.");
        }

        if (document.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("Protected history exceeds its entry bound.");
        }
    }

    private static void ValidateEntry(ProtectedHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateMetadata(entry.CreatedAtUtc, entry.ProcessName, entry.Category);
        if (string.IsNullOrWhiteSpace(entry.ProtectedText) ||
            entry.ProtectedText.Length > MaximumProtectedTextCharacters)
        {
            throw new InvalidDataException(
                "Protected history ciphertext is outside the accepted bounds.");
        }
    }

    private static void ValidateMetadata(
        DateTimeOffset createdAtUtc,
        string processName,
        string category)
    {
        if (createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Protected history timestamps must be UTC.");
        }

        if (string.IsNullOrWhiteSpace(processName) ||
            processName.Length > 128 ||
            processName.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "Protected history process metadata is outside the accepted bounds.");
        }

        if (string.IsNullOrWhiteSpace(category) ||
            category.Length > 32 ||
            category.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidDataException(
                "Protected history category metadata is outside the accepted bounds.");
        }
    }

    private static void ValidateRetentionDays(int retentionDays)
    {
        if (retentionDays is < 1 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                "Protected history retention must be between 1 and 90 days.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _store.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private sealed class ProtectedHistoryDocument
    {
        public int Version { get; set; } = CurrentVersion;

        public List<ProtectedHistoryEntry> Entries { get; set; } = [];
    }

    private sealed class ProtectedHistoryEntry
    {
        public DateTimeOffset CreatedAtUtc { get; set; }

        public string ProcessName { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string ProtectedText { get; set; } = string.Empty;
    }
}

public sealed record ProtectedHistoryRecord(
    DateTimeOffset CreatedAtUtc,
    string ProcessName,
    string Category,
    string Text);
