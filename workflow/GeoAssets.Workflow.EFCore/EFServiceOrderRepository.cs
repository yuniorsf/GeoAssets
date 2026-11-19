using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IServiceOrderRepository"/>.
///
/// Features are stored as a JSON array of IDs. Pass an <see cref="IAssetProvider"/>
/// to hydrate full <see cref="GeoAssets.Core.Models.GeoFeature"/> objects on load;
/// when omitted, <see cref="ServiceOrder.Features"/> will be empty and IDs are
/// still accessible via <see cref="ServiceOrder.FeatureIds"/>.
/// </summary>
public sealed class EFServiceOrderRepository : IServiceOrderRepository, IAsyncDisposable
{
    private readonly ServiceOrderDbContext _db;
    private readonly IAssetProvider? _assets;
    private readonly OrderTypeRegistry? _orderTypeRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly IOrderLifecycleHook[] _hooks;

    /// <param name="timeProvider">
    /// Clock for the release stamp an order's targets get when the order reaches a terminal
    /// state (see <see cref="ServiceOrderTargetRelease"/>); the system clock when omitted.
    /// </param>
    /// <param name="hooks">
    /// Modules' side effects of a status change (XD01-362), run before the change is saved and in the same transaction —
    /// see <see cref="SaveTransitionAsync"/>.
    /// </param>
    public EFServiceOrderRepository(
        ServiceOrderDbContext db,
        IAssetProvider? assets = null,
        OrderTypeRegistry? orderTypeRegistry = null,
        TimeProvider? timeProvider = null,
        IEnumerable<IOrderLifecycleHook>? hooks = null)
    {
        _db = db;
        _assets = assets;
        _orderTypeRegistry = orderTypeRegistry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hooks = hooks?.ToArray() ?? [];
    }

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>? OrderAdded;
    public event EventHandler<IServiceOrder>? OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
    public event EventHandler<string>? OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    // Single-record fetches below (GetByIdAsync, GetParentAsync's own-id lookup, and the
    // write-path fetches further down) deliberately call IgnoreQueryFilters(): unlike
    // GeoEntityRow, ServiceOrderRecord has a real cross-org delegation mechanism
    // (CrossOrgGrantRule/WorkflowOrgGrant) that the REST layer evaluates against the fetched
    // record — see ServiceOrderDbContext.OnModelCreating's own doc comment. The tenant-equality
    // query filter is for the list/search surface (LoadAllAsync and its callers), not these.

    public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var record = await _db.ServiceOrders
            .IgnoreQueryFilters()
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .FirstOrDefaultAsync(o => o.Id == id, ct).ConfigureAwait(false);

        if (record is null) return null;

        var childIds = await _db.ServiceOrders
            .IgnoreQueryFilters()
            .Where(o => o.ParentOrderId == id)
            .Select(o => o.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        return ServiceOrderMapper.ToDomain(record, childIds, _assets);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.ParentOrderId == null), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.ParentOrderId == parentId), ct);

    public async Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
    {
        var parentId = await _db.ServiceOrders
            .IgnoreQueryFilters()
            .Where(o => o.Id == childId)
            .Select(o => o.ParentOrderId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return parentId is null ? null : await GetByIdAsync(parentId, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.Status == status), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.AssignedTo == userId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.CreatedBy == userId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.OrderTypeId == orderTypeId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.CreatedAt >= from && o.CreatedAt <= toExclusive), ct);

    /// <summary>
    /// The orders dispatched to the given target. Among the caller's own organization's orders
    /// that is whatever target is asked for. The one case that reaches past the tenant filter is
    /// the inbox of an organization (XD01-316): when the target asked for <i>is</i> this
    /// context's organization, orders of other organizations dispatched to it are included —
    /// the application-layer twin of the dispatch-recipient row-level-security policy. Asking for
    /// any other organization never reaches past the filter, so the target id a caller supplies
    /// cannot widen what it sees.
    /// </summary>
    public async Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
    {
        var isOwnInbox = IsOwnOrganization(targetId, targetType, out var spellings);

        // An organization's inbox is matched on both conventional spellings of its id (lower and
        // upper case); any other target id is matched exactly as it is asked for.
        var dispatches = isOwnInbox
            ? _db.OrderDispatches.Where(d => d.TargetType == (int)targetType && spellings.Contains(d.TargetId))
            : _db.OrderDispatches.Where(d => d.TargetType == (int)targetType && d.TargetId == targetId);

        var orderIds = await dispatches
            .Select(d => d.ServiceOrderId)
            .Distinct()
            .ToHashSetAsync(ct).ConfigureAwait(false);

        var orders = isOwnInbox ? _db.ServiceOrders.IgnoreQueryFilters() : _db.ServiceOrders;
        return await LoadAllAsync(orders.Where(o => orderIds.Contains(o.Id)), ct).ConfigureAwait(false);
    }

    private bool IsOwnOrganization(string targetId, DispatchTargetType targetType, out string[] spellings)
    {
        spellings = [];
        if (targetType != DispatchTargetType.Organization) return false;
        if (!Guid.TryParse(targetId, out var organizationId) || organizationId == Guid.Empty) return false;
        if (organizationId != _db.CurrentOrganizationId) return false;

        var canonical = organizationId.ToString("D");
        spellings = [canonical, canonical.ToUpperInvariant()];
        return true;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        _db.ServiceOrders.Add(ServiceOrderMapper.ToRecord(so));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        OrderAdded?.Invoke(this, order);
    }

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        var existing = await _db.ServiceOrders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == order.Id, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

        var previous = existing.Status;
        var orderType = _orderTypeRegistry?.Find(so.OrderTypeId);

        if (!ServiceOrderTransitions.IsValid(orderType, previous, so.Status))
            throw new InvalidServiceOrderTransitionException(previous, so.Status);

        UpdateRecord(existing, so);

        // Use the caller's own RowVersion — from whenever they last read this order — as the
        // optimistic-concurrency precondition, instead of the value `existing` was just
        // re-queried with a few lines above. That's what makes this catch a writer who has held
        // a stale in-memory copy since a much earlier read, not just a race within this call's
        // own read-then-save window (XD01-26). Empty means the caller never actually read the
        // order from a store (e.g. immediately after AddAsync) — fall back to the previous,
        // narrower behavior rather than rejecting the write.
        if (so.RowVersion.Length > 0)
            _db.Entry(existing).Property(e => e.RowVersion).OriginalValue = so.RowVersion;

        try
        {
            await SaveTransitionAsync(so, orderType, previous, so.Status, ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ServiceOrderConcurrencyException(order.Id);
        }

        OrderUpdated?.Invoke(this, order);

        if (!string.Equals(previous, order.Status, StringComparison.Ordinal))
            OrderStatusChanged?.Invoke(this, (order, previous));
    }

    public async Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var record = await _db.ServiceOrders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        _db.OrderDispatches.Add(new OrderDispatchRecord
        {
            ServiceOrderId = orderId,
            TargetId = dispatch.TargetId,
            TargetType = (int)dispatch.TargetType,
            DispatchedBy = dispatch.DispatchedBy,
            DispatchedAt = dispatch.DispatchedAt,
            Note = dispatch.Note,
        });
        record.UpdatedAt = dispatch.DispatchedAt;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var updated = await GetByIdAsync(orderId, ct).ConfigureAwait(false);
        OrderUpdated?.Invoke(this, updated!);
    }

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var record = await _db.ServiceOrders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        var previous = record.Status;
        var orderType = _orderTypeRegistry?.Find(record.OrderTypeId);

        if (entry.ResultingStatus is not null && !ServiceOrderTransitions.IsValid(orderType, previous, entry.ResultingStatus))
            throw new InvalidServiceOrderTransitionException(previous, entry.ResultingStatus);

        _db.OrderActionLogs.Add(new OrderActionLogRecord
        {
            ServiceOrderId = orderId,
            Action = StoredOrderAction.NumberOf(entry.Action),
            ActionCode = StoredOrderAction.CodeOf(entry.Action),
            PerformedBy = entry.PerformedBy,
            PerformedAt = entry.PerformedAt,
            Comment = entry.Comment,
            ResultingStatus = entry.ResultingStatus,
        });

        if (entry.ResultingStatus is not null)
        {
            record.Status = entry.ResultingStatus;
            record.UpdatedAt = entry.PerformedAt;
            if (ServiceOrderTransitions.IsSuccessState(orderType, entry.ResultingStatus))
                record.CompletedAt = entry.PerformedAt;
        }
        else
        {
            record.UpdatedAt = entry.PerformedAt;
        }

        // An entry without a ResultingStatus keeps the status: no release, no hooks.
        await SaveTransitionAsync(
            ServiceOrderMapper.ToDomain(record, []), orderType, previous, entry.ResultingStatus ?? previous, ct).ConfigureAwait(false);

        var updated = await GetByIdAsync(orderId, ct).ConfigureAwait(false);
        OrderUpdated?.Invoke(this, updated!);

        if (entry.ResultingStatus is not null && !string.Equals(entry.ResultingStatus, previous, StringComparison.Ordinal))
            OrderStatusChanged?.Invoke(this, (updated!, previous));
    }

    /// <summary>
    /// Saves a write that may move the order from <paramref name="previous"/> to <paramref name="next"/> as one unit:
    /// the release stamp of its targets when it closes (XD01-306), then — on an actual status change — every
    /// <see cref="IOrderLifecycleHook"/> (XD01-362), then <c>SaveChanges</c>. With hooks on a relational store this runs in
    /// a transaction (the caller's, when it has one), so a hook's own writes commit or roll back with the transition; any
    /// failure undoes the tracked release stamp too, so a later save on this context cannot release the targets of an
    /// order that never moved.
    /// </summary>
    private async Task SaveTransitionAsync(ServiceOrder order, OrderType? orderType, string previous, string next, CancellationToken ct)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var releasedTargets = await ServiceOrderTargetRelease
            .StampIfClosingAsync(_db, order.Id, orderType, previous, next, occurredAt, ct).ConfigureAwait(false);

        var transition = string.Equals(previous, next, StringComparison.Ordinal) || _hooks.Length == 0
            ? null
            : new OrderTransitionContext(order, orderType, previous, next, occurredAt);
        var transaction = transition is not null && _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        var trackedBeforeHooks = transition is null ? null : PendingEntities();
        try
        {
            if (transition is not null)
            {
                foreach (var hook in _hooks)
                    await hook.OnTransitionedAsync(transition, ct).ConfigureAwait(false);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ServiceOrderTargetRelease.Discard(_db, releasedTargets);
            if (trackedBeforeHooks is not null)
                UndoHookChanges(trackedBeforeHooks);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The entities with changes pending in the context, before the hooks run.</summary>
    private HashSet<object> PendingEntities() =>
        _db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// After a failed save, undoes in the change tracker what the hooks staged (XD01-362) — rows they added are detached, rows
    /// they modified or deleted go back to their original values — so a later save on this context cannot persist a hook's
    /// effect of a transition that never happened.
    /// </summary>
    private void UndoHookChanges(HashSet<object> pendingBeforeHooks)
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => !pendingBeforeHooks.Contains(e.Entity)).ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // FindAsync (unlike IgnoreQueryFilters().FirstOrDefaultAsync) applies the global query
        // filter when nothing matching is already tracked, which would wrongly block deleting a
        // cross-org grant target — see the comment above GetByIdAsync.
        var record = await _db.ServiceOrders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct).ConfigureAwait(false);
        if (record is null) return;

        _db.ServiceOrders.Remove(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        OrderDeleted?.Invoke(this, id);
    }

    // ── Async disposal ────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Loads a filtered set of orders with their children, dispatches, and action log.</summary>
    private async Task<IReadOnlyList<IServiceOrder>> LoadAllAsync(
        IQueryable<ServiceOrderRecord> query, CancellationToken ct)
    {
        var records = await query
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        if (records.Count == 0) return [];

        var ids = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var childMap = (await _db.ServiceOrders
            .Where(o => o.ParentOrderId != null && ids.Contains(o.ParentOrderId!))
            .Select(o => new { o.Id, ParentId = o.ParentOrderId! })
            .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.ParentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Id).ToList(), StringComparer.Ordinal);

        return records
            .Select(r => (IServiceOrder)ServiceOrderMapper.ToDomain(
                r,
                childMap.TryGetValue(r.Id, out var c) ? c : [],
                _assets))
            .ToList();
    }

    /// <summary>
    /// Updates the scalar fields of an existing tracked <paramref name="target"/>
    /// record in-place from the domain <paramref name="source"/>.
    /// Does not touch Dispatches or ActionLog — see <see cref="AppendDispatchAsync"/>
    /// and <see cref="AppendActionAsync"/>. Does not touch <c>OrganizationId</c> (nor
    /// <c>CreatedBy</c>/<c>CreatedAt</c>) either: an order's organization is fixed when it is
    /// created (XD01-332), so whatever <paramref name="source"/> states, the stored value stays.
    /// </summary>
    private static void UpdateRecord(ServiceOrderRecord target, ServiceOrder source)
    {
        target.Title = source.Title;
        target.Description = source.Description;
        target.OrderTypeId = source.OrderTypeId;
        target.Status = source.Status;
        target.Priority = (int)source.Priority;
        target.AssignedTo = source.AssignedTo;
        target.UpdatedAt = source.UpdatedAt;
        target.ScheduledAt = source.ScheduledAt;
        target.CompletedAt = source.CompletedAt;
        target.ParentOrderId = source.ParentOrderId;
        target.AttributesJson = System.Text.Json.JsonSerializer.Serialize(source.Attributes);
        target.FeatureIdsJson = System.Text.Json.JsonSerializer.Serialize(
                                     source.Features.Count > 0
                                         ? source.Features.Select(f => f.Id).ToArray()
                                         : source.FeatureIds);
        target.SelectionSpecJson = ServiceOrderMapper.ToSelectionSpecJson(source.SelectionSpec);
    }
}
