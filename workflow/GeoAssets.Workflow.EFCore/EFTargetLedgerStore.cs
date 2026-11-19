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
/// EF Core implementation of <see cref="ITargetLedgerStore"/> (XD01-379) over the same <c>ServiceOrderTargets</c> table
/// the in-process <see cref="EFServiceOrderTargetRepository"/> uses: the ledger is that table keyed by
/// <c>(VerticalId, ServiceOrderId)</c>. The contract is the member documentation on the port.
///
/// <para><b>Tenancy.</b> Every member ignores the tenant query filter and scopes by the organization it is given: the
/// callers are service callers acting for a vertical, whose request names the organization. The global usage search
/// is unscoped by design. On PostgreSQL the row-level-security policy is the backstop.</para>
///
/// <para><b>Writes return what they changed.</b> Each is one statement with <c>RETURNING</c> (understood by PostgreSQL
/// and SQLite), so the rows reported are exactly the rows this call inserted or changed, even against a concurrent
/// writer — never a read-then-write guess.</para>
/// </summary>
public sealed class EFTargetLedgerStore : ITargetLedgerStore
{
    private readonly ServiceOrderDbContext _db;
    private readonly OrderTypeRegistry? _orderTypeRegistry;

    /// <param name="db">The scope's context, shared with the outbox so a change and its event are one transaction.</param>
    /// <param name="orderTypeRegistry">Source of in-process order types' exclusivity for the global usage search.</param>
    public EFTargetLedgerStore(ServiceOrderDbContext db, OrderTypeRegistry? orderTypeRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        _db = db;
        _orderTypeRegistry = orderTypeRegistry;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TargetLedgerUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, TargetLedgerKey excluding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentNullException.ThrowIfNull(excluding);
        ct.ThrowIfCancellationRequested();

        var wanted = featureIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0) return [];

        var usages = new List<TargetLedgerUsage>();
        foreach (var chunk in wanted.Chunk(EFServiceOrderTargetRepository.IdChunkSize))
        {
            usages.AddRange(await KernelUsagesAsync(chunk, excluding, ct).ConfigureAwait(false));
            usages.AddRange(await RemoteUsagesAsync(chunk, excluding, ct).ConfigureAwait(false));
        }

        return usages;
    }

    public Task<Guid?> FindOrganizationAsync(TargetLedgerKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        return TargetsOf(key)
            .Select(t => (Guid?)t.OrganizationId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<TargetLedgerEntry>> GetPageAsync(
        TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ct.ThrowIfCancellationRequested();

        var matches = TargetsOf(key).Where(t => t.OrganizationId == organizationId);
        var totalCount = await matches.CountAsync(ct).ConfigureAwait(false);
        if (skip >= totalCount)
            return new PagedResult<TargetLedgerEntry> { Items = [], TotalCount = totalCount };

        var records = await matches
            .OrderBy(t => t.FeatureId).ThenBy(t => t.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<TargetLedgerEntry>
        {
            Items = records
                .Select(r => new TargetLedgerEntry(
                    ServiceOrderTargetMapper.ToDomain(r),
                    r.ReleasedAt is { } releasedAt ? ServiceOrderTargetMapper.ToUtc(releasedAt) : null))
                .ToList(),
            TotalCount = totalCount,
        };
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Takes a transaction-scoped advisory lock per feature (<c>pg_advisory_xact_lock(class, hashtext(feature))</c>, class
    /// <see cref="FeatureLockClass"/>, ordered by key) on PostgreSQL; must run inside the unit of work's transaction. Other
    /// providers (SQLite tests) have a single writer and take no lock.
    /// </summary>
    public async Task LockFeaturesAsync(IReadOnlyCollection<string> featureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ct.ThrowIfCancellationRequested();

        if (featureIds.Count == 0 || !string.Equals(_db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal)) return;

        var ids = featureIds.Distinct(StringComparer.Ordinal).ToArray();
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, k) FROM (SELECT DISTINCT hashtext(f) AS k FROM unnest({1}::text[]) AS f ORDER BY k) AS keys",
            [FeatureLockClass, ids],
            ct).ConfigureAwait(false);
    }

    /// <summary>The advisory-lock class (first key) of the target ledger's per-feature reservation locks.</summary>
    public const int FeatureLockClass = 379;

    public async Task<IReadOnlyList<ServiceOrderTarget>> ReserveAsync(
        TargetLedgerOrder order, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();

        if (targets.Any(t => t is null || !BelongsTo(t, order)))
        {
            throw new ArgumentException(
                $"Every target must belong to order '{order.OrderId}' of vertical '{order.VerticalId}' and its organization.", nameof(targets));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = targets
            .Where(t => seen.Add(t.FeatureId))
            .Select(t => ServiceOrderTargetMapper.ToRecord(t, releasedAt: null, order.OrderTypeId, order.Policy.Exclusivity))
            .ToList();

        var inserted = new List<ServiceOrderTarget>(records.Count);
        foreach (var batch in records.Chunk(EFServiceOrderTargetRepository.InsertRowsPerStatement))
        {
            var (sql, parameters) = EFServiceOrderTargetRepository.BuildInsertIfAbsent(_db, batch, returningAll: true);
            inserted.AddRange(await RunReturningAsync(sql, parameters, ct).ConfigureAwait(false));
        }

        return inserted;
    }

    public Task<IReadOnlyList<ServiceOrderTarget>> ReleaseAsync(
        TargetLedgerKey key, Guid? organizationId, IReadOnlyCollection<string>? targetIds, DateTimeOffset releasedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        var statement = new Statement(_db);
        var set = $"{statement.Column(nameof(ServiceOrderTargetRecord.ReleasedAt))} = {statement.Parameter(nameof(ServiceOrderTargetRecord.ReleasedAt), releasedAt.UtcDateTime)}";

        return UpdateOpenAsync(statement, set, key, organizationId, targetIds, extraCondition: null, ct);
    }

    public Task<IReadOnlyList<ServiceOrderTarget>> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        var statement = new Statement(_db);
        var status = statement.Column(nameof(ServiceOrderTargetRecord.Status));
        var version = statement.Column(nameof(ServiceOrderTargetRecord.Version));
        var set = $"{status} = {statement.Parameter(nameof(ServiceOrderTargetRecord.Status), OrderTargetStatus.Done)}, {version} = {version} + 1";
        var unfinished =
            $"{status} IN ({statement.Parameter(nameof(ServiceOrderTargetRecord.Status), OrderTargetStatus.Pending)}, " +
            $"{statement.Parameter(nameof(ServiceOrderTargetRecord.Status), OrderTargetStatus.InProgress)})";

        return UpdateOpenAsync(statement, set, key, organizationId, targetIds, unfinished, ct);
    }

    /// <summary>The stored exclusivity of a remote vertical's target; an absent or unknown value counts as none.</summary>
    internal static OrderTargetExclusivity ToExclusivity(int? stored) =>
        stored is { } value && Enum.IsDefined((OrderTargetExclusivity)value) ? (OrderTargetExclusivity)value : OrderTargetExclusivity.None;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<ServiceOrderTargetRecord> TargetsOf(TargetLedgerKey key)
    {
        var (verticalId, orderId) = (key.VerticalId, key.OrderId);
        return _db.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.VerticalId == verticalId && t.ServiceOrderId == orderId);
    }

    private static bool BelongsTo(ServiceOrderTarget target, TargetLedgerOrder order) =>
        string.Equals(target.VerticalId, order.VerticalId, StringComparison.Ordinal)
        && string.Equals(target.OrderId, order.OrderId, StringComparison.Ordinal)
        && target.OrganizationId == order.OrganizationId;

    /// <summary>Open usages held by in-process orders, which carry their type, status and parent on the order row.</summary>
    private async Task<IEnumerable<TargetLedgerUsage>> KernelUsagesAsync(string[] chunk, TargetLedgerKey excluding, CancellationToken ct)
    {
        var kernel = TargetLedger.KernelVerticalId;
        var excludedOrderId = TargetLedger.IsKernel(excluding.VerticalId) ? excluding.OrderId : null;
        var rows = await (
            from target in _db.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            join order in _db.ServiceOrders.IgnoreQueryFilters() on target.ServiceOrderId equals order.Id
            where chunk.Contains(target.FeatureId)
                && target.VerticalId == kernel
                && target.ReleasedAt == null
                && (excludedOrderId == null || target.ServiceOrderId != excludedOrderId)
            select new { target.FeatureId, target.OrganizationId, order.Id, order.OrderTypeId, order.Status, order.ParentOrderId })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(row => new TargetLedgerUsage(
            new OrderTargetUsage(
                row.FeatureId,
                row.Id,
                row.OrderTypeId,
                row.Status,
                row.ParentOrderId,
                _orderTypeRegistry?.Find(row.OrderTypeId)?.TargetPolicy?.Exclusivity ?? OrderTargetExclusivity.None),
            row.OrganizationId));
    }

    /// <summary>Open usages held by remote verticals' orders, with the type and exclusivity stored at reservation.</summary>
    private async Task<IEnumerable<TargetLedgerUsage>> RemoteUsagesAsync(string[] chunk, TargetLedgerKey excluding, CancellationToken ct)
    {
        var kernel = TargetLedger.KernelVerticalId;
        var (excludedVertical, excludedOrder) = (excluding.VerticalId, excluding.OrderId);
        var rows = await _db.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            .Where(t => chunk.Contains(t.FeatureId)
                && t.VerticalId != kernel
                && t.ReleasedAt == null
                && !(t.VerticalId == excludedVertical && t.ServiceOrderId == excludedOrder))
            .Select(t => new { t.FeatureId, t.ServiceOrderId, t.OrderTypeId, t.Exclusivity, t.VerticalId, t.OrganizationId })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .DistinctBy(row => (row.FeatureId, row.VerticalId, row.ServiceOrderId))
            .Select(row => new TargetLedgerUsage(
                new OrderTargetUsage(
                    row.FeatureId,
                    row.ServiceOrderId,
                    row.OrderTypeId ?? string.Empty,
                    string.Empty,
                    null,
                    ToExclusivity(row.Exclusivity),
                    row.VerticalId),
                row.OrganizationId));
    }

    /// <summary>
    /// <c>UPDATE … SET <paramref name="set"/> WHERE</c> the open targets of <paramref name="key"/> (in
    /// <paramref name="organizationId"/> when given, among <paramref name="targetIds"/> when given, meeting
    /// <paramref name="extraCondition"/> when given) <c>RETURNING *</c>.
    /// </summary>
    private async Task<IReadOnlyList<ServiceOrderTarget>> UpdateOpenAsync(
        Statement statement,
        string set,
        TargetLedgerKey key,
        Guid? organizationId,
        IReadOnlyCollection<string>? targetIds,
        string? extraCondition,
        CancellationToken ct)
    {
        var ids = targetIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids is { Length: 0 }) return [];

        var where = new StringBuilder()
            .Append(statement.Column(nameof(ServiceOrderTargetRecord.VerticalId))).Append(" = ")
            .Append(statement.Parameter(nameof(ServiceOrderTargetRecord.VerticalId), key.VerticalId))
            .Append(" AND ").Append(statement.Column(nameof(ServiceOrderTargetRecord.ServiceOrderId))).Append(" = ")
            .Append(statement.Parameter(nameof(ServiceOrderTargetRecord.ServiceOrderId), key.OrderId))
            .Append(" AND ").Append(statement.Column(nameof(ServiceOrderTargetRecord.ReleasedAt))).Append(" IS NULL");
        if (organizationId is { } organization)
        {
            where.Append(" AND ").Append(statement.Column(nameof(ServiceOrderTargetRecord.OrganizationId))).Append(" = ")
                .Append(statement.Parameter(nameof(ServiceOrderTargetRecord.OrganizationId), organization));
        }

        if (extraCondition is not null) where.Append(" AND ").Append(extraCondition);

        if (ids is null)
            return await RunReturningAsync($"UPDATE {statement.Table} SET {set} WHERE {where} RETURNING *", statement.Parameters, ct).ConfigureAwait(false);

        var changed = new List<ServiceOrderTarget>();
        foreach (var chunk in ids.Chunk(EFServiceOrderTargetRepository.IdChunkSize))
        {
            var chunkStatement = statement.Fork();
            var inList = string.Join(", ", chunk.Select(id => chunkStatement.Parameter(nameof(ServiceOrderTargetRecord.Id), id)));
            var sql = $"UPDATE {statement.Table} SET {set} WHERE {where} AND {statement.Column(nameof(ServiceOrderTargetRecord.Id))} IN ({inList}) RETURNING *";
            changed.AddRange(await RunReturningAsync(sql, chunkStatement.Parameters, ct).ConfigureAwait(false));
        }

        return changed;
    }

    /// <summary>
    /// Runs a data-changing statement ending in <c>RETURNING *</c> and maps the rows it returned. Nothing composes over the
    /// SQL — no query filter, no tracking — so EF sends it exactly as written.
    /// </summary>
    private async Task<List<ServiceOrderTarget>> RunReturningAsync(string sql, IReadOnlyList<object> parameters, CancellationToken ct)
    {
        var records = await _db.ServiceOrderTargets
            .FromSqlRaw(sql, [.. parameters])
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ServiceOrderTargetMapper.ToDomain).ToList();
    }

    /// <summary>
    /// Builds the parts of a statement on <c>ServiceOrderTargets</c>: identifiers delimited as the provider writes them
    /// and parameters typed by their column's own mapping, named <c>@l0</c>, <c>@l1</c>, … in order of creation.
    /// </summary>
    private sealed class Statement
    {
        private readonly ServiceOrderDbContext _db;
        private readonly IEntityType _entityType;
        private readonly StoreObjectIdentifier _table;
        private readonly ISqlGenerationHelper _sql;
        private readonly List<object> _parameters;

        public Statement(ServiceOrderDbContext db)
            : this(db, [])
        {
        }

        private Statement(ServiceOrderDbContext db, List<object> parameters)
        {
            _db = db;
            _entityType = db.Model.FindEntityType(typeof(ServiceOrderTargetRecord))
                ?? throw new InvalidOperationException($"{nameof(ServiceOrderTargetRecord)} is not part of the model.");
            _table = StoreObjectIdentifier.Table(_entityType.GetTableName()!, _entityType.GetSchema());
            _sql = db.GetService<ISqlGenerationHelper>();
            _parameters = parameters;
        }

        public string Table => _sql.DelimitIdentifier(_table.Name, _table.Schema);

        public IReadOnlyList<object> Parameters => _parameters;

        /// <summary>A copy holding the parameters created so far, to which a variant of the statement adds its own.</summary>
        public Statement Fork() => new(_db, [.. _parameters]);

        public string Column(string property) => _sql.DelimitIdentifier(_entityType.GetProperty(property).GetColumnName(_table)!);

        public string Parameter(string property, object? value)
        {
            var column = _entityType.GetProperty(property);
            var name = _sql.GenerateParameterName(string.Create(CultureInfo.InvariantCulture, $"l{_parameters.Count}"));

            // Only a factory for provider-typed parameters; it is never executed.
            using var factory = _db.Database.GetDbConnection().CreateCommand();
            _parameters.Add(column.GetRelationalTypeMapping().CreateParameter(factory, name, value, column.IsNullable));
            return name;
        }
    }
}
