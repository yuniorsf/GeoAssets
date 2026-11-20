using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Persistence.Entities;
using GeoAssets.NeutralOperator.Application.Occupancy;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IOccupancyChangeSetReader"/> and
/// <see cref="IOccupancyChangeSetWriter"/> over the <c>ServiceOrderOccupancyChangeSets</c>
/// table (XD01-315). The contract it honours is the member documentation on those two
/// interfaces; what follows is only how.
///
/// <para><b>Tenancy.</b> Owner only, uniformly: no member calls <c>IgnoreQueryFilters()</c>, so
/// every read and write sees just the sets — and the targets — of the context's organization
/// (<see cref="NeutralOperatorDbContext.CurrentOrganizationId"/>). On PostgreSQL the table's
/// row-level-security policy is the backstop.</para>
///
/// <para><b>Staging is delete-then-insert inside one transaction</b> (the caller's when the
/// shared connection already has one — the kernel's, which the context joins first through
/// <see cref="NeutralOperatorDbContext.JoinAmbientTransaction"/> — its own otherwise), past the
/// change tracker. The partial
/// unique index on the staged row of a target is what arbitrates two sessions: the later
/// insert is skipped (<c>ON CONFLICT DO NOTHING</c>), and that session then replaces the row
/// the earlier one committed — so every caller succeeds and one whole set remains.</para>
///
/// <para><b>Closing an order</b> discards what is still staged on it through
/// <see cref="DiscardStagedAsync"/> — one <c>UPDATE</c> in the transaction the order's closing
/// transition runs in, which the order's lifecycle hook calls before that transition is saved
/// (XD01-362); the two commit or roll back together. Since XD01-380 this store's context is not
/// the order's, so the discard cannot ride on the order's <c>SaveChanges</c> as a tracked change.</para>
/// </summary>
public sealed class EFOccupancyChangeSetRepository : IOccupancyChangeSetReader, IOccupancyChangeSetWriter, IOccupancyChangeSetCloser
{
    /// <summary>
    /// How often a staging retries after losing the insert to another session. Each retry
    /// follows another session's committed insert, so only that many concurrent stagings of
    /// one target could exhaust it.
    /// </summary>
    internal const int MaxStageAttempts = 32;

    /// <summary>Every mapped column of <see cref="OccupancyChangeSetRecord"/>, with how to read its value.</summary>
    internal static readonly IReadOnlyList<(string Property, Func<OccupancyChangeSetRecord, object?> Value)> Columns =
    [
        (nameof(OccupancyChangeSetRecord.Id), r => r.Id),
        (nameof(OccupancyChangeSetRecord.ServiceOrderId), r => r.ServiceOrderId),
        (nameof(OccupancyChangeSetRecord.TargetId), r => r.TargetId),
        (nameof(OccupancyChangeSetRecord.PoleFeatureId), r => r.PoleFeatureId),
        (nameof(OccupancyChangeSetRecord.ChangesJson), r => r.ChangesJson),
        (nameof(OccupancyChangeSetRecord.BaselineVersion), r => r.BaselineVersion),
        (nameof(OccupancyChangeSetRecord.Status), r => r.Status),
        (nameof(OccupancyChangeSetRecord.StagedBy), r => r.StagedBy),
        (nameof(OccupancyChangeSetRecord.StagedAt), r => r.StagedAt),
        (nameof(OccupancyChangeSetRecord.ClosedAt), r => r.ClosedAt),
        (nameof(OccupancyChangeSetRecord.OrganizationId), r => r.OrganizationId),
        (nameof(OccupancyChangeSetRecord.ApplyBatchId), r => r.ApplyBatchId),
    ];

    internal static readonly string StagedStatus = OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Staged);

    private static readonly string DiscardedStatus = OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Discarded);

    private static readonly string CommittedStatus = OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Committed);

    private readonly NeutralOperatorDbContext _db;
    private readonly TimeProvider _timeProvider;

    /// <param name="db">The scoped context, on the connection shared with the order and target repositories; its tenant is the pole owner.</param>
    /// <param name="timeProvider">Clock for the instant a set is discarded; the system clock when omitted.</param>
    public EFOccupancyChangeSetRepository(NeutralOperatorDbContext db, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var stagedStatus = StagedStatus;
        var record = await Sets
            .Where(s => s.ServiceOrderId == orderId && s.TargetId == targetId && s.Status == stagedStatus)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return record is null ? null : OccupancyChangeSetMapper.ToDomain(record);
    }

    public async Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var orderId = query.OrderId;
        var matches = Sets.Where(s => s.ServiceOrderId == orderId);
        if (query.Status is { } status)
        {
            var statusText = OccupancyChangeSetMapper.ToStatusText(status);
            matches = matches.Where(s => s.Status == statusText);
        }

        var totalCount = await matches.CountAsync(ct).ConfigureAwait(false);
        var skip = query.Skip;
        if (skip >= totalCount)
            return new PagedResult<OccupancyChangeSet> { Items = [], TotalCount = totalCount };

        // A uuid column (PostgreSQL) and its upper-case text form (SQLite) both order like the
        // ordinal order of the id's canonical text, which is what the contract names.
        var records = await matches
            .OrderBy(s => s.PoleFeatureId)
            .ThenBy(s => s.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<OccupancyChangeSet>
        {
            Items = [.. records.Select(OccupancyChangeSetMapper.ToDomain)],
            TotalCount = totalCount,
        };
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<OccupancyChangeSet> StageAsync(OccupancyChangeSet changeSet, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        if (changeSet.Status != OccupancyChangeSetStatus.Staged)
            throw new ArgumentException($"Only a staged change set can be staged; this one is {changeSet.Status}.", nameof(changeSet));
        if (changeSet.PoleOwnerOrganizationId != _db.CurrentOrganizationId)
            throw new ArgumentException("The change set belongs to another organization than the store's.", nameof(changeSet));

        await EnsureStageableAsync(changeSet, ct).ConfigureAwait(false);

        var record = OccupancyChangeSetMapper.ToRecord(changeSet);
        if (_db.Database.CurrentTransaction is not null)
        {
            await ReplaceStagedAsync(record, ct).ConfigureAwait(false);
            return OccupancyChangeSetMapper.ToDomain(record);
        }

        var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await ReplaceStagedAsync(record, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return OccupancyChangeSetMapper.ToDomain(record);
    }

    public async Task<bool> DiscardAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var stagedStatus = StagedStatus;
        var discarded = await DiscardWhereAsync(
            _db.OccupancyChangeSets.Where(s => s.ServiceOrderId == orderId && s.TargetId == targetId && s.Status == stagedStatus), ct)
            .ConfigureAwait(false);
        return discarded > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unfiltered, like the order the caller is closing: whoever may close it discards all of its staged sets. One
    /// <c>UPDATE</c>, past the change tracker, in the transaction the closing transition runs in (joined first).
    /// </remarks>
    public Task DiscardStagedAsync(string orderId, DateTimeOffset closedAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        cancellationToken.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var (stagedStatus, discardedStatus) = (StagedStatus, DiscardedStatus);
        DateTime? closedAtStamp = OccupancyChangeSetMapper.WholeMicroseconds(closedAt);
        return _db.OccupancyChangeSets.IgnoreQueryFilters()
            .Where(s => s.ServiceOrderId == orderId && s.Status == stagedStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, discardedStatus)
                    .SetProperty(s => s.ClosedAt, closedAtStamp),
                cancellationToken);
    }

    public Task<int> DiscardAllAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var stagedStatus = StagedStatus;
        return DiscardWhereAsync(
            _db.OccupancyChangeSets.Where(s => s.ServiceOrderId == orderId && s.Status == stagedStatus), ct);
    }

    /// <summary>
    /// One <c>UPDATE … WHERE Id = @id AND Status = 'Staged'</c>, past the change tracker, in the
    /// context's transaction when it has one: a set replaced, discarded or committed by another
    /// session in between matches no row, so this returns false and changes nothing.
    /// </summary>
    public async Task<bool> MarkCommittedAsync(OccupancyChangeSet committed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        if (committed.Status != OccupancyChangeSetStatus.Committed)
            throw new ArgumentException($"Only a committed change set can be recorded as committed; this one is {committed.Status}.", nameof(committed));
        if (committed.PoleOwnerOrganizationId != _db.CurrentOrganizationId)
            throw new ArgumentException("The change set belongs to another organization than the store's.", nameof(committed));

        var (setId, stagedStatus) = (committed.Id, StagedStatus);
        var committedStatus = CommittedStatus;
        DateTime? closedAt = OccupancyChangeSetMapper.WholeMicroseconds(committed.ClosedAt!.Value);
        var updated = await _db.OccupancyChangeSets
            .Where(s => s.Id == setId && s.Status == stagedStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, committedStatus)
                    .SetProperty(s => s.ClosedAt, closedAt),
                ct).ConfigureAwait(false);

        return updated == 1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<OccupancyChangeSetRecord> Sets => _db.OccupancyChangeSets.AsNoTracking();

    /// <summary>The target must be the organization's, on that order, for that pole, and not released; the set's id must be new or still staged.</summary>
    private async Task EnsureStageableAsync(OccupancyChangeSet changeSet, CancellationToken ct)
    {
        var (orderId, targetId, poleFeatureId, setId) = (changeSet.OrderId, changeSet.TargetId, changeSet.PoleFeatureId, changeSet.Id);

        var target = await _db.ServiceOrderTargets.AsNoTracking()
            .Where(t => t.Id == targetId && t.ServiceOrderId == orderId && t.FeatureId == poleFeatureId)
            .Select(t => new { t.ReleasedAt })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Service order '{orderId}' has no target '{targetId}' for pole '{poleFeatureId}'.");

        if (target.ReleasedAt is not null)
            throw new OccupancyTargetReleasedException(orderId, targetId);

        var stagedStatus = StagedStatus;
        if (await Sets.AnyAsync(s => s.Id == setId && s.Status != stagedStatus, ct).ConfigureAwait(false))
            throw new ArgumentException($"Change set '{setId}' is already on file and no longer staged.", nameof(changeSet));
    }

    /// <summary>
    /// Removes the target's staged row and inserts <paramref name="record"/>. When another
    /// session's insert got in between (the insert is then skipped by the partial unique
    /// index), the next round removes that session's row instead.
    /// </summary>
    private async Task ReplaceStagedAsync(OccupancyChangeSetRecord record, CancellationToken ct)
    {
        var (targetId, stagedStatus) = (record.TargetId, StagedStatus);
        for (var attempt = 0; attempt < MaxStageAttempts; attempt++)
        {
            await _db.OccupancyChangeSets
                .Where(s => s.TargetId == targetId && s.Status == stagedStatus)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);

            var inserted = await RawRowInsert.ExecuteAsync(_db, [record], Columns, skipOnConflict: [], ct).ConfigureAwait(false);
            if (inserted == 1) return;
        }

        throw new InvalidOperationException(
            $"Could not stage occupancy changes for target '{targetId}' after {MaxStageAttempts} attempts: too many sessions are staging for it at once.");
    }

    private Task<int> DiscardWhereAsync(IQueryable<OccupancyChangeSetRecord> staged, CancellationToken ct)
    {
        var discardedStatus = DiscardedStatus;
        DateTime? closedAt = OccupancyChangeSetMapper.WholeMicroseconds(_timeProvider.GetUtcNow());
        return staged.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(s => s.Status, discardedStatus)
                .SetProperty(s => s.ClosedAt, closedAt),
            ct);
    }
}
