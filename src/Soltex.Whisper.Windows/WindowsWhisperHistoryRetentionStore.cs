using Soltex.Security;

namespace Soltex.Whisper.Windows;

public sealed class WindowsWhisperHistoryRetentionStore : IWhisperHistoryRetentionStore
{
    private readonly AuthenticatedProtectedHistoryStore _store;

    public WindowsWhisperHistoryRetentionStore(string authenticatedStateRoot)
    {
        _store = new AuthenticatedProtectedHistoryStore(authenticatedStateRoot);
    }

    public async ValueTask<IReadOnlyList<WhisperHistoryEntry>> LoadAsync(
        int retentionDays,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProtectedHistoryRecord> records = await _store.LoadAsync(
            retentionDays,
            cancellationToken).ConfigureAwait(false);
        List<WhisperHistoryEntry> entries = new(records.Count);
        foreach (ProtectedHistoryRecord record in records)
        {
            if (!Enum.TryParse(
                    record.Category,
                    ignoreCase: false,
                    out WhisperDeliveryKind deliveryKind) ||
                !Enum.IsDefined(deliveryKind))
            {
                throw new InvalidDataException(
                    "Protected Whisper history contains an unsupported delivery category.");
            }

            entries.Add(new WhisperHistoryEntry(
                record.CreatedAtUtc,
                record.ProcessName,
                deliveryKind,
                record.Text));
        }

        return entries.AsReadOnly();
    }

    public async ValueTask SaveAsync(
        IReadOnlyCollection<WhisperHistoryEntry> entries,
        int retentionDays,
        CancellationToken cancellationToken) =>
        await _store.SaveAsync(
            MapBounded(entries),
            retentionDays,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask RewriteAsync(
        IReadOnlyCollection<WhisperHistoryEntry> entries,
        int retentionDays,
        CancellationToken cancellationToken) =>
        await _store.ReplaceAsync(
            MapBounded(entries),
            retentionDays,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask ClearAsync(CancellationToken cancellationToken) =>
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync() =>
        await _store.DisposeAsync().ConfigureAwait(false);

    private static ProtectedHistoryRecord[] MapBounded(
        IReadOnlyCollection<WhisperHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .TakeLast(AuthenticatedProtectedHistoryStore.MaximumEntries)
            .Select(entry => new ProtectedHistoryRecord(
                entry.CreatedAtUtc,
                entry.ProcessName,
                entry.DeliveryKind.ToString(),
                entry.Text))
            .ToArray();
    }
}
