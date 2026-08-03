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
    private readonly IAssetProvider?    _assets;

    public EFServiceOrderRepository(ServiceOrderDbContext db, IAssetProvider? assets = null)
    {
        _db     = db;
        _assets = assets;
    }

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>?                                      OrderAdded;
    public event EventHandler<IServiceOrder>?                                      OrderUpdated;
    public event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged;
    public event EventHandler<string>?                                             OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var record = await _db.ServiceOrders
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (record is null) return null;

        var childIds = await _db.ServiceOrders
            .Where(o => o.ParentOrderId == id)
            .Select(o => o.Id)
            .ToListAsync(ct);

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
            .Where(o => o.Id == childId)
            .Select(o => o.ParentOrderId)
            .FirstOrDefaultAsync(ct);

        return parentId is null ? null : await GetByIdAsync(parentId, ct);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.Status == (int)status), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.AssignedTo == userId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.CreatedBy == userId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.OrderTypeId == orderTypeId), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => LoadAllAsync(_db.ServiceOrders.Where(o => o.CreatedAt >= from && o.CreatedAt <= to), ct);

    public async Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
    {
        var orderIds = await _db.OrderDispatches
            .Where(d => d.TargetId == targetId && d.TargetType == (int)targetType)
            .Select(d => d.ServiceOrderId)
            .Distinct()
            .ToHashSetAsync(ct);

        return await LoadAllAsync(_db.ServiceOrders.Where(o => orderIds.Contains(o.Id)), ct);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        _db.ServiceOrders.Add(ServiceOrderMapper.ToRecord(so));
        await _db.SaveChangesAsync(ct);

        OrderAdded?.Invoke(this, order);
    }

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        var previous = await _db.ServiceOrders
            .AsNoTracking()
            .Where(o => o.Id == order.Id)
            .Select(o => (ServiceOrderStatus)o.Status)
            .FirstOrDefaultAsync(ct);

        var existing = await _db.ServiceOrders.FirstOrDefaultAsync(o => o.Id == order.Id, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

        if (!ServiceOrderTransitions.IsValid(previous, so.Status))
            throw new InvalidServiceOrderTransitionException(previous, so.Status);

        UpdateRecord(existing, so);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ServiceOrderConcurrencyException(order.Id);
        }

        OrderUpdated?.Invoke(this, order);

        if (previous != order.Status)
            OrderStatusChanged?.Invoke(this, (order, previous));
    }

    public async Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        var record = await _db.ServiceOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        _db.OrderDispatches.Add(new OrderDispatchRecord
        {
            ServiceOrderId = orderId,
            TargetId       = dispatch.TargetId,
            TargetType     = (int)dispatch.TargetType,
            DispatchedBy   = dispatch.DispatchedBy,
            DispatchedAt   = dispatch.DispatchedAt,
            Note           = dispatch.Note,
        });
        record.UpdatedAt = dispatch.DispatchedAt;

        await _db.SaveChangesAsync(ct);

        var updated = await GetByIdAsync(orderId, ct);
        OrderUpdated?.Invoke(this, updated!);
    }

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        var record = await _db.ServiceOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        var previous = (ServiceOrderStatus)record.Status;

        if (entry.ResultingStatus.HasValue && !ServiceOrderTransitions.IsValid(previous, entry.ResultingStatus.Value))
            throw new InvalidServiceOrderTransitionException(previous, entry.ResultingStatus.Value);

        _db.OrderActionLogs.Add(new OrderActionLogRecord
        {
            ServiceOrderId  = orderId,
            Action          = (int)entry.Action,
            PerformedBy     = entry.PerformedBy,
            PerformedAt     = entry.PerformedAt,
            Comment         = entry.Comment,
            ResultingStatus = entry.ResultingStatus.HasValue ? (int)entry.ResultingStatus.Value : null,
        });

        if (entry.ResultingStatus.HasValue)
        {
            record.Status    = (int)entry.ResultingStatus.Value;
            record.UpdatedAt = entry.PerformedAt;
            if (entry.ResultingStatus.Value == ServiceOrderStatus.Completed)
                record.CompletedAt = entry.PerformedAt;
        }
        else
        {
            record.UpdatedAt = entry.PerformedAt;
        }

        await _db.SaveChangesAsync(ct);

        var updated = await GetByIdAsync(orderId, ct);
        OrderUpdated?.Invoke(this, updated!);

        if (entry.ResultingStatus.HasValue && entry.ResultingStatus.Value != previous)
            OrderStatusChanged?.Invoke(this, (updated!, previous));
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var record = await _db.ServiceOrders.FindAsync([id], ct);
        if (record is null) return;

        _db.ServiceOrders.Remove(record);
        await _db.SaveChangesAsync(ct);

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
            .ToListAsync(ct);

        if (records.Count == 0) return [];

        var ids       = records.Select(r => r.Id).ToHashSet();
        var childMap  = (await _db.ServiceOrders
            .Where(o => o.ParentOrderId != null && ids.Contains(o.ParentOrderId!))
            .Select(o => new { o.Id, ParentId = o.ParentOrderId! })
            .ToListAsync(ct))
            .GroupBy(x => x.ParentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Id).ToList());

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
    /// and <see cref="AppendActionAsync"/>.
    /// </summary>
    private static void UpdateRecord(ServiceOrderRecord target, ServiceOrder source)
    {
        target.Title           = source.Title;
        target.Description     = source.Description;
        target.OrderTypeId     = source.OrderTypeId;
        target.Status          = (int)source.Status;
        target.Priority        = (int)source.Priority;
        target.AssignedTo      = source.AssignedTo;
        target.UpdatedAt       = source.UpdatedAt;
        target.ScheduledAt     = source.ScheduledAt;
        target.CompletedAt     = source.CompletedAt;
        target.ParentOrderId   = source.ParentOrderId;
        target.AttributesJson  = System.Text.Json.JsonSerializer.Serialize(source.Attributes);
        target.FeatureIdsJson  = System.Text.Json.JsonSerializer.Serialize(
                                     source.Features.Count > 0
                                         ? source.Features.Select(f => f.Id).ToArray()
                                         : source.FeatureIds);
        target.SelectionSpecJson = source.SelectionSpec is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(source.SelectionSpec);
    }
}
