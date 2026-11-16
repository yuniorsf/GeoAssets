using System.Globalization;
using System.Text;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IServiceOrderTargetReader"/> and
/// <see cref="IServiceOrderTargetWriter"/> over the <c>ServiceOrderTargets</c> table (XD01-306).
/// The contract it honours is the member documentation on those two interfaces; what follows
/// is only how.
///
/// <para><b>Tenancy.</b> Every operation addressed to one order ignores the tenant query filter,
/// for the reason <see cref="EFServiceOrderRepository"/>'s single-record fetches do: the caller
/// has already authorized the order itself, possibly through a cross-organization grant, and a
/// tenant-equality filter would hide a granted order's targets. <see cref="FindOpenUsagesAsync"/>,
/// the one search across orders, looks only inside the organization that owns the excluded
/// order (the caller's own organization when that order does not exist yet). On PostgreSQL the
/// row-level-security policy on the table is the backstop for all of it.</para>
///
/// <para><b>Writes bypass the change tracker</b> (one <c>INSERT … ON CONFLICT DO NOTHING</c>,
/// <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>), so each is atomic in the database rather than a
/// read-then-save: two connections adding the same feature both succeed with one row stored,
/// and of two connections updating the same version exactly one wins. The insert's
/// <c>ON CONFLICT</c> clause is understood by PostgreSQL and SQLite, the two engines this
/// context runs on.</para>
/// </summary>
public sealed class EFServiceOrderTargetRepository : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    /// <summary>How many ids go into one <c>IN (…)</c> list before a lookup or delete is split.</summary>
    internal const int IdChunkSize = 1000;

    /// <summary>How many rows go into one insert statement (16 parameters each).</summary>
    internal const int InsertRowsPerStatement = 100;

    /// <summary>Every mapped column of <see cref="ServiceOrderTargetRecord"/>, with how to read its value.</summary>
    internal static readonly IReadOnlyList<(string Property, Func<ServiceOrderTargetRecord, object?> Value)> InsertColumns =
    [
        (nameof(ServiceOrderTargetRecord.Id), r => r.Id),
        (nameof(ServiceOrderTargetRecord.ServiceOrderId), r => r.ServiceOrderId),
        (nameof(ServiceOrderTargetRecord.FeatureId), r => r.FeatureId),
        (nameof(ServiceOrderTargetRecord.AssetTypeId), r => r.AssetTypeId),
        (nameof(ServiceOrderTargetRecord.Alias), r => r.Alias),
        (nameof(ServiceOrderTargetRecord.ActivityIdsJson), r => r.ActivityIdsJson),
        (nameof(ServiceOrderTargetRecord.Status), r => r.Status),
        (nameof(ServiceOrderTargetRecord.ConflictLevel), r => r.ConflictLevel),
        (nameof(ServiceOrderTargetRecord.Note), r => r.Note),
        (nameof(ServiceOrderTargetRecord.Longitude), r => r.Longitude),
        (nameof(ServiceOrderTargetRecord.Latitude), r => r.Latitude),
        (nameof(ServiceOrderTargetRecord.AddedAt), r => r.AddedAt),
        (nameof(ServiceOrderTargetRecord.AddedBy), r => r.AddedBy),
        (nameof(ServiceOrderTargetRecord.OrganizationId), r => r.OrganizationId),
        (nameof(ServiceOrderTargetRecord.Version), r => r.Version),
        (nameof(ServiceOrderTargetRecord.ReleasedAt), r => r.ReleasedAt),
    ];

    private readonly ServiceOrderDbContext _db;
    private readonly OrderTypeRegistry? _orderTypeRegistry;
    private readonly TimeProvider _timeProvider;

    /// <param name="db">The same scoped context the order repository uses.</param>
    /// <param name="orderTypeRegistry">
    /// Source of each holding order type's exclusivity and terminal states. Without it every
    /// usage reports <see cref="OrderTargetExclusivity.None"/> and the default order graph decides
    /// what is terminal.
    /// </param>
    /// <param name="timeProvider">Clock for the release stamp of targets added to an already-closed order.</param>
    public EFServiceOrderTargetRepository(
        ServiceOrderDbContext db,
        OrderTypeRegistry? orderTypeRegistry = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        _db = db;
        _orderTypeRegistry = orderTypeRegistry;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var matches = TargetsOf(query.OrderId);
        if (query.Status is { } status)
            matches = matches.Where(t => t.Status == status);
        if (query.ConflictLevel is { } conflictLevel)
        {
            var level = (int)conflictLevel;
            matches = matches.Where(t => t.ConflictLevel == level);
        }

        var totalCount = await matches.CountAsync(ct).ConfigureAwait(false);
        var skip = query.Skip;
        if (skip >= totalCount)
            return new PagedResult<ServiceOrderTarget> { Items = [], TotalCount = totalCount };

        var records = await Sort(matches, OrderTargetSortKeys.Resolve(query.SortBy), query.Descending)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<ServiceOrderTarget>
        {
            Items = records.Select(ServiceOrderTargetMapper.ToDomain).ToList(),
            TotalCount = totalCount,
        };
    }

    public async Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();

        var record = await TargetsOf(orderId)
            .FirstOrDefaultAsync(t => t.Id == targetId, ct).ConfigureAwait(false);

        return record is null ? null : ServiceOrderTargetMapper.ToDomain(record);
    }

    public Task<int> CountAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        return TargetsOf(orderId).CountAsync(ct);
    }

    public async Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludingOrderId);
        ct.ThrowIfCancellationRequested();

        var wanted = featureIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0) return [];

        var owningOrganizationId = await _db.ServiceOrders.IgnoreQueryFilters()
            .Where(o => o.Id == excludingOrderId)
            .Select(o => (Guid?)o.OrganizationId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // IgnoreQueryFilters() switches off the filter of every entity in the query, so the
        // branch that scopes by the excluded order's organization states that scope itself, and
        // the other branch keeps both tenant filters (targets and the joined orders).
        var candidates = owningOrganizationId is { } organizationId
            ? _db.ServiceOrderTargets.IgnoreQueryFilters().Where(t => t.OrganizationId == organizationId)
            : _db.ServiceOrderTargets;

        var usages = new List<OrderTargetUsage>();
        foreach (var chunk in wanted.Chunk(IdChunkSize))
        {
            var rows = await (
                from target in candidates.AsNoTracking()
                join order in _db.ServiceOrders on target.ServiceOrderId equals order.Id
                where chunk.Contains(target.FeatureId)
                    && target.ReleasedAt == null
                    && target.ServiceOrderId != excludingOrderId
                select new { target.FeatureId, order.Id, order.OrderTypeId, order.Status, order.ParentOrderId })
                .ToListAsync(ct).ConfigureAwait(false);

            usages.AddRange(rows.Select(row => new OrderTargetUsage(
                row.FeatureId,
                row.Id,
                row.OrderTypeId,
                row.Status,
                row.ParentOrderId,
                _orderTypeRegistry?.Find(row.OrderTypeId)?.TargetPolicy?.Exclusivity ?? OrderTargetExclusivity.None)));
        }

        return usages;
    }

    public async Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        var box = await TargetsOf(orderId)
            .Where(t => t.Longitude != null && t.Latitude != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MinLongitude = g.Min(t => t.Longitude),
                MinLatitude = g.Min(t => t.Latitude),
                MaxLongitude = g.Max(t => t.Longitude),
                MaxLatitude = g.Max(t => t.Latitude),
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return box is { MinLongitude: { } minLongitude, MinLatitude: { } minLatitude, MaxLongitude: { } maxLongitude, MaxLatitude: { } maxLatitude }
            ? new OrderTargetExtent(minLongitude, minLatitude, maxLongitude, maxLatitude)
            : null;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();

        if (targets.Count == 0) return 0;

        if (targets.Any(target => target is null || !string.Equals(target.OrderId, orderId, StringComparison.Ordinal)))
            throw new ArgumentException($"Every target must belong to ServiceOrder '{orderId}'.", nameof(targets));

        var order = await _db.ServiceOrders.IgnoreQueryFilters()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.OrderTypeId, o.Status, o.OrganizationId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        if (targets.Any(target => target.OrganizationId != order.OrganizationId))
            throw new ArgumentException($"Every target must carry the organization of ServiceOrder '{orderId}'.", nameof(targets));

        // A target joining an order that is already closed is born released: "released" means
        // the owning order reached a terminal state, whichever happened first.
        DateTime? releasedAt = ServiceOrderTransitions.IsTerminalState(_orderTypeRegistry?.Find(order.OrderTypeId), order.Status)
            ? _timeProvider.GetUtcNow().UtcDateTime
            : null;

        var seenFeatureIds = new HashSet<string>(StringComparer.Ordinal);
        var records = targets
            .Where(target => seenFeatureIds.Add(target.FeatureId))
            .Select(target => ServiceOrderTargetMapper.ToRecord(target, releasedAt))
            .ToList();

        return await InsertIfAbsentAsync(records, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();

        var targetId = target.Id;
        var orderId = target.OrderId;
        var expectedVersion = target.Version;
        var nextVersion = checked(expectedVersion + 1);
        var activityIdsJson = ServiceOrderTargetMapper.ToActivityIdsJson(target.ActivityIds);
        var status = target.Status;
        var conflictLevel = (int)target.ConflictLevel;
        var note = target.Note;

        // One compare-and-swap statement: it changes the row only while the row still carries
        // the version the caller read, so of two writers holding the same version one updates a
        // row and the other updates none — on any number of connections.
        var updated = await _db.ServiceOrderTargets.IgnoreQueryFilters()
            .Where(t => t.Id == targetId && t.ServiceOrderId == orderId && t.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.ActivityIdsJson, activityIdsJson)
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.ConflictLevel, conflictLevel)
                    .SetProperty(t => t.Note, note)
                    .SetProperty(t => t.Version, nextVersion),
                ct).ConfigureAwait(false);

        if (updated > 0) return;

        var exists = await _db.ServiceOrderTargets.IgnoreQueryFilters()
            .AnyAsync(t => t.Id == targetId && t.ServiceOrderId == orderId, ct).ConfigureAwait(false);

        if (!exists)
            throw new KeyNotFoundException($"ServiceOrderTarget '{targetId}' not found in ServiceOrder '{orderId}'.");

        throw new ServiceOrderConcurrencyException(orderId);
    }

    public async Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targetIds);
        ct.ThrowIfCancellationRequested();

        var ids = targetIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return 0;

        var removed = 0;
        foreach (var chunk in ids.Chunk(IdChunkSize))
        {
            removed += await _db.ServiceOrderTargets.IgnoreQueryFilters()
                .Where(t => t.ServiceOrderId == orderId && chunk.Contains(t.Id))
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        return removed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<ServiceOrderTargetRecord> TargetsOf(string orderId) =>
        _db.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking().Where(t => t.ServiceOrderId == orderId);

    /// <summary>
    /// The total order of the reader contract: the resolved key in the requested direction, then
    /// always the id ascending. Null aliases are forced after non-null ones when ascending and
    /// before them when descending, because PostgreSQL and SQLite disagree on where nulls go.
    /// </summary>
    private static IQueryable<ServiceOrderTargetRecord> Sort(IQueryable<ServiceOrderTargetRecord> targets, string sortKey, bool descending)
    {
        var sorted = (sortKey, descending) switch
        {
            (OrderTargetSortKeys.Alias, false) => targets.OrderBy(t => t.Alias == null).ThenBy(t => t.Alias),
            (OrderTargetSortKeys.Alias, true) => targets.OrderByDescending(t => t.Alias == null).ThenByDescending(t => t.Alias),
            (OrderTargetSortKeys.Status, false) => targets.OrderBy(t => t.Status),
            (OrderTargetSortKeys.Status, true) => targets.OrderByDescending(t => t.Status),
            (OrderTargetSortKeys.ConflictLevel, false) => targets.OrderBy(t => t.ConflictLevel),
            (OrderTargetSortKeys.ConflictLevel, true) => targets.OrderByDescending(t => t.ConflictLevel),
            (OrderTargetSortKeys.AddedAt, false) => targets.OrderBy(t => t.AddedAt),
            (OrderTargetSortKeys.AddedAt, true) => targets.OrderByDescending(t => t.AddedAt),
            (_, false) => targets.OrderBy(t => t.FeatureId),
            (_, true) => targets.OrderByDescending(t => t.FeatureId),
        };

        return sorted.ThenBy(t => t.Id);
    }

    /// <summary>
    /// Inserts <paramref name="records"/>, letting the database skip any whose
    /// (order, feature) pair is already stored, and returns how many rows were really written.
    /// </summary>
    private async Task<int> InsertIfAbsentAsync(List<ServiceOrderTargetRecord> records, CancellationToken ct)
    {
        var batches = records.Chunk(InsertRowsPerStatement).ToList();

        // More than one statement must still be all-or-nothing; a caller's own transaction
        // already guarantees that, so only open one when there is none.
        if (batches.Count <= 1 || _db.Database.CurrentTransaction is not null)
            return await InsertBatchesAsync(batches, ct).ConfigureAwait(false);

        var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var inserted = await InsertBatchesAsync(batches, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return inserted;
        }
    }

    /// <summary>
    /// One <c>INSERT … VALUES (…), (…) ON CONFLICT (order, feature) DO NOTHING</c> per batch.
    /// Table and column names come from the EF model and every value travels as a parameter
    /// typed by that column's own mapping, so the statement cannot drift from the schema.
    /// </summary>
    private async Task<int> InsertBatchesAsync(List<ServiceOrderTargetRecord[]> batches, CancellationToken ct)
    {
        var entityType = _db.Model.FindEntityType(typeof(ServiceOrderTargetRecord))
            ?? throw new InvalidOperationException($"{nameof(ServiceOrderTargetRecord)} is not part of the model.");
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sql = _db.GetService<ISqlGenerationHelper>();
        var columns = InsertColumns.Select(column => entityType.GetProperty(column.Property)).ToList();

        string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(table)!);

        var head = $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} ({string.Join(", ", columns.Select(Column))}) VALUES ";
        var tail =
            $" ON CONFLICT ({Column(entityType.GetProperty(nameof(ServiceOrderTargetRecord.ServiceOrderId)))}, " +
            $"{Column(entityType.GetProperty(nameof(ServiceOrderTargetRecord.FeatureId)))}) DO NOTHING";

        // Only a factory for provider-typed parameters; it is never executed.
        var parameterFactory = _db.Database.GetDbConnection().CreateCommand();
        await using (parameterFactory.ConfigureAwait(false))
        {
            var inserted = 0;
            foreach (var batch in batches)
            {
                var statement = new StringBuilder(head);
                var parameters = new List<object>(batch.Length * columns.Count);
                for (var row = 0; row < batch.Length; row++)
                {
                    statement.Append(row == 0 ? "(" : ", (");
                    for (var column = 0; column < columns.Count; column++)
                    {
                        var name = sql.GenerateParameterName(string.Create(CultureInfo.InvariantCulture, $"t{row}_{column}"));
                        var value = InsertColumns[column].Value(batch[row]);
                        parameters.Add(columns[column].GetRelationalTypeMapping().CreateParameter(parameterFactory, name, value, columns[column].IsNullable));
                        statement.Append(column == 0 ? string.Empty : ", ").Append(name);
                    }

                    statement.Append(')');
                }

                statement.Append(tail);
                inserted += await _db.Database.ExecuteSqlRawAsync(statement.ToString(), parameters, ct).ConfigureAwait(false);
            }

            return inserted;
        }
    }
}
