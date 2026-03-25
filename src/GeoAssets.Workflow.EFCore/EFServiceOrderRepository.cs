using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IServiceOrderRepository"/>.
///
/// Features are stored as a JSON array of IDs. Pass an <see cref="IAssetRepository"/>
/// to hydrate full <see cref="GeoAssets.Core.Models.GeoFeature"/> objects on load;
/// when omitted, <see cref="ServiceOrder.Features"/> will be empty and IDs are
/// still accessible via <see cref="ServiceOrder.FeatureIds"/>.
/// </summary>
public sealed class EFServiceOrderRepository : IServiceOrderRepository, IAsyncDisposable
{
    private readonly ServiceOrderDbContext _db;
    private readonly IAssetRepository?    _assets;

    public EFServiceOrderRepository(ServiceOrderDbContext db, IAssetRepository? assets = null)
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

    public IServiceOrder? GetById(string id)
    {
        var record = _db.ServiceOrders
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .FirstOrDefault(o => o.Id == id);

        if (record is null) return null;

        var childIds = _db.ServiceOrders
            .Where(o => o.ParentOrderId == id)
            .Select(o => o.Id)
            .ToList();

        return ServiceOrderMapper.ToDomain(record, childIds, _assets);
    }

    public IReadOnlyList<IServiceOrder> GetAll()
        => LoadAll(_db.ServiceOrders);

    public IReadOnlyList<IServiceOrder> GetRoots()
        => LoadAll(_db.ServiceOrders.Where(o => o.ParentOrderId == null));

    public IReadOnlyList<IServiceOrder> GetChildren(string parentId)
        => LoadAll(_db.ServiceOrders.Where(o => o.ParentOrderId == parentId));

    public IServiceOrder? GetParent(string childId)
    {
        var parentId = _db.ServiceOrders
            .Where(o => o.Id == childId)
            .Select(o => o.ParentOrderId)
            .FirstOrDefault();

        return parentId is null ? null : GetById(parentId);
    }

    public IReadOnlyList<IServiceOrder> GetByStatus(ServiceOrderStatus status)
        => LoadAll(_db.ServiceOrders.Where(o => o.Status == (int)status));

    public IReadOnlyList<IServiceOrder> GetByAssignee(string userId)
        => LoadAll(_db.ServiceOrders.Where(o => o.AssignedTo == userId));

    public IReadOnlyList<IServiceOrder> GetByCreator(string userId)
        => LoadAll(_db.ServiceOrders.Where(o => o.CreatedBy == userId));

    public IReadOnlyList<IServiceOrder> GetByOrderType(string orderTypeId)
        => LoadAll(_db.ServiceOrders.Where(o => o.OrderTypeId == orderTypeId));

    public IReadOnlyList<IServiceOrder> GetByDateRange(DateTime from, DateTime to)
        => LoadAll(_db.ServiceOrders.Where(o => o.CreatedAt >= from && o.CreatedAt <= to));

    public IReadOnlyList<IServiceOrder> GetDispatchedTo(string targetId, DispatchTargetType targetType)
    {
        var orderIds = _db.OrderDispatches
            .Where(d => d.TargetId == targetId && d.TargetType == (int)targetType)
            .Select(d => d.ServiceOrderId)
            .Distinct()
            .ToHashSet();

        return LoadAll(_db.ServiceOrders.Where(o => orderIds.Contains(o.Id)));
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public void Add(IServiceOrder order)
    {
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        _db.ServiceOrders.Add(ServiceOrderMapper.ToRecord(so));
        _db.SaveChanges();

        OrderAdded?.Invoke(this, order);
    }

    public void Update(IServiceOrder order)
    {
        if (order is not ServiceOrder so)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        var previous = _db.ServiceOrders
            .AsNoTracking()
            .Where(o => o.Id == order.Id)
            .Select(o => (ServiceOrderStatus)o.Status)
            .FirstOrDefault();

        var existing = _db.ServiceOrders
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .First(o => o.Id == order.Id);

        UpdateRecord(existing, so);
        _db.SaveChanges();

        OrderUpdated?.Invoke(this, order);

        if (previous != order.Status)
            OrderStatusChanged?.Invoke(this, (order, previous));
    }

    public void Delete(string id)
    {
        var record = _db.ServiceOrders.Find(id);
        if (record is null) return;

        _db.ServiceOrders.Remove(record);
        _db.SaveChanges();

        OrderDeleted?.Invoke(this, id);
    }

    // ── Async disposal ────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Loads a filtered set of orders with their children, dispatches, and action log.</summary>
    private IReadOnlyList<IServiceOrder> LoadAll(IQueryable<ServiceOrderRecord> query)
    {
        var records = query
            .Include(o => o.Dispatches)
            .Include(o => o.ActionLog)
            .OrderBy(o => o.CreatedAt)
            .ToList();

        if (records.Count == 0) return [];

        var ids       = records.Select(r => r.Id).ToHashSet();
        var childMap  = _db.ServiceOrders
            .Where(o => o.ParentOrderId != null && ids.Contains(o.ParentOrderId!))
            .Select(o => new { o.Id, ParentId = o.ParentOrderId! })
            .ToList()
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
    /// Updates an existing tracked <paramref name="target"/> record in-place
    /// from the domain <paramref name="source"/>.
    /// Dispatches and ActionLog rows are synced by id: new rows are added,
    /// existing rows are updated.
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

        // Sync dispatches: remove deleted, add new (dispatches are append-only by design)
        var existingDispatchCount = target.Dispatches.Count;
        foreach (var d in source.Dispatches.Skip(existingDispatchCount))
        {
            target.Dispatches.Add(new OrderDispatchRecord
            {
                TargetId     = d.TargetId,
                TargetType   = (int)d.TargetType,
                DispatchedBy = d.DispatchedBy,
                DispatchedAt = d.DispatchedAt,
                Note         = d.Note,
            });
        }

        // Sync action log: append-only
        var existingLogCount = target.ActionLog.Count;
        foreach (var a in source.ActionLog.Skip(existingLogCount))
        {
            target.ActionLog.Add(new OrderActionLogRecord
            {
                Action          = (int)a.Action,
                PerformedBy     = a.PerformedBy,
                PerformedAt     = a.PerformedAt,
                Comment         = a.Comment,
                ResultingStatus = a.ResultingStatus.HasValue ? (int)a.ResultingStatus.Value : null,
            });
        }
    }
}
