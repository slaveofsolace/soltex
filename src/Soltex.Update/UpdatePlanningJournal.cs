using Soltex.Security;

namespace Soltex.Update;

public sealed class UpdatePlanningJournal : IDisposable
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 128;
    private readonly AuthenticatedJsonStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public UpdatePlanningJournal(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        _store = new AuthenticatedJsonStore(Path.GetFullPath(stateRoot), "update-planning-journal");
    }

    internal string StatePath => _store.StatePath;
    internal string BackupPath => _store.BackupPath;

    public async Task AppendAsync(
        UpdatePlanningJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateEntry(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JournalState state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            List<UpdatePlanningJournalEntry> entries = state.Entries
                .Where(item => item.AttemptId != entry.AttemptId || item.Phase != entry.Phase)
                .ToList();
            entries.Add(entry with { Detail = UpdateText.Sanitize(entry.Detail) });
            entries = entries
                .OrderBy(item => item.TimestampUtc)
                .ThenBy(item => item.AttemptId)
                .ThenBy(item => item.Phase)
                .TakeLast(MaximumEntries)
                .ToList();
            await _store.SaveAsync(
                new JournalState(SchemaVersion, entries),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UpdatePlanningJournalEntry>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JournalState state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Entries
                .OrderBy(item => item.TimestampUtc)
                .ThenBy(item => item.AttemptId)
                .ThenBy(item => item.Phase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UpdatePlanningRecoveryReport> InspectAsync(
        string stagingParent,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UpdatePlanningJournalEntry> entries = await ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        List<string> existingTokens = entries
            .Where(item => UpdatePrivateStaging.IsToken(item.PrivateStagingToken))
            .Select(item => item.PrivateStagingToken!)
            .Distinct(StringComparer.Ordinal)
            .Where(token => Directory.Exists(
                UpdatePrivateStaging.GetRootForToken(stagingParent, token)))
            .Order(StringComparer.Ordinal)
            .ToList();
        bool incomplete = entries
            .GroupBy(item => item.AttemptId)
            .Any(group => group.OrderBy(item => item.TimestampUtc).Last().Phase is not
                UpdatePlanningPhase.PreviewReady and not
                UpdatePlanningPhase.Cleaned);
        return new UpdatePlanningRecoveryReport(
            entries,
            existingTokens.AsReadOnly(),
            incomplete,
            incomplete || existingTokens.Count > 0
                ? "One or more non-activating planning attempts require cleanup review."
                : "No incomplete update-planning attempt is recorded.");
    }

    public async Task CleanupPlanningArtifactsAsync(
        Guid attemptId,
        string stagingParent,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UpdatePlanningJournalEntry> entries = await ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (string token in entries
                     .Where(item => item.AttemptId == attemptId &&
                                    UpdatePrivateStaging.IsToken(item.PrivateStagingToken))
                     .Select(item => item.PrivateStagingToken!)
                     .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdatePrivateStaging.DeleteTree(
                UpdatePrivateStaging.GetRootForToken(stagingParent, token));
        }

        await AppendAsync(
            new UpdatePlanningJournalEntry(
                attemptId,
                UpdatePlanningPhase.Cleaned,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null,
                "Private planning artifacts were removed; no installation state existed."),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JournalState> LoadStateAsync(CancellationToken cancellationToken)
    {
        JournalState state = await _store.LoadAsync(
            static () => new JournalState(SchemaVersion, []),
            cancellationToken).ConfigureAwait(false);
        if (state.SchemaVersion != SchemaVersion ||
            state.Entries is null ||
            state.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("The update planning journal schema is invalid.");
        }

        foreach (UpdatePlanningJournalEntry? entry in state.Entries)
        {
            ValidateEntry(entry);
        }

        return state;
    }

    private static void ValidateEntry(UpdatePlanningJournalEntry? entry)
    {
        if (entry is null ||
            entry.AttemptId == Guid.Empty ||
            entry.TimestampUtc == default ||
            entry.TimestampUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(entry.Detail) ||
            entry.Detail.Length > UpdateText.MaximumDetailLength ||
            (!string.IsNullOrWhiteSpace(entry.DescriptorSha256) &&
             !UpdateText.IsSha256(entry.DescriptorSha256)) ||
            (!string.IsNullOrWhiteSpace(entry.ManifestSha256) &&
             !UpdateText.IsSha256(entry.ManifestSha256)) ||
            (!string.IsNullOrWhiteSpace(entry.PackageSha256) &&
             !UpdateText.IsSha256(entry.PackageSha256)) ||
            (!string.IsNullOrWhiteSpace(entry.PrivateStagingToken) &&
             !UpdatePrivateStaging.IsToken(entry.PrivateStagingToken)))
        {
            throw new InvalidDataException("The update planning journal contains an invalid entry.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _store.Dispose();
        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record JournalState(
        int SchemaVersion,
        List<UpdatePlanningJournalEntry> Entries);
}
