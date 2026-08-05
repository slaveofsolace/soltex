namespace Soltex.Security;

public sealed class AllowListStore
{
    private const int MaximumEntries = 512;
    private readonly AuthenticatedJsonStore _store;

    internal AllowListStore(AuthenticatedJsonStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<AllowListEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        AllowListDocument document = await _store.LoadAsync(
            static () => new AllowListDocument([]),
            cancellationToken).ConfigureAwait(false);
        return document.Entries.OrderByDescending(entry => entry.AddedAtUtc).ToArray();
    }

    public async Task<bool> ContainsAsync(string sha256, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeHash(sha256);
        IReadOnlyList<AllowListEntry> entries = await ListAsync(cancellationToken).ConfigureAwait(false);
        return entries.Any(entry => string.Equals(entry.Sha256, normalized, StringComparison.Ordinal));
    }

    public async Task AddAsync(
        string sha256,
        string displayName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeHash(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        List<AllowListEntry> entries = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        entries.RemoveAll(entry => string.Equals(entry.Sha256, normalized, StringComparison.Ordinal));
        entries.Insert(0, new AllowListEntry(
            normalized,
            Limit(displayName, 160),
            Limit(reason, 500),
            DateTimeOffset.UtcNow));

        if (entries.Count > MaximumEntries)
        {
            entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
        }

        await _store.SaveAsync(new AllowListDocument(entries), cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string sha256, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeHash(sha256);
        List<AllowListEntry> entries = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        entries.RemoveAll(entry => string.Equals(entry.Sha256, normalized, StringComparison.Ordinal));
        await _store.SaveAsync(new AllowListDocument(entries), cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 hash must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        return normalized;
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private sealed record AllowListDocument(List<AllowListEntry> Entries);
}
