using GeoAssets.Workflow.Orders;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>EF Core implementation of <see cref="IOrderTypeRepository"/>.</summary>
public sealed class EFOrderTypeRepository(ServiceOrderDbContext db) : IOrderTypeRepository
{
    public async Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var record = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return record is null ? null : OrderTypeMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .OrderBy(t => t.DisplayName)
            .ToListAsync(ct);

        return records.Select(OrderTypeMapper.ToDomain).ToList();
    }

    public async Task AddAsync(OrderType orderType, CancellationToken ct = default)
        => await db.OrderTypes.AddAsync(OrderTypeMapper.ToRecord(orderType), ct);

    public async Task UpdateAsync(OrderType orderType, CancellationToken ct = default)
    {
        var existing = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .FirstOrDefaultAsync(t => t.Id == orderType.Id, ct);

        if (existing is null) return;

        existing.DisplayName     = orderType.DisplayName;
        existing.Description     = orderType.Description;
        existing.InitialStateKey = orderType.InitialStateKey;

        // Replace child collections (simplest correct strategy for small sets)
        db.RemoveRange(existing.CreationPolicies);
        db.RemoveRange(existing.ActionPermissions);
        db.RemoveRange(existing.States);
        db.RemoveRange(existing.Transitions);

        existing.CreationPolicies = orderType.CreationPolicies
            .Select(p => new Entities.OrderCreationPolicyRecord
            {
                OrderTypeId = orderType.Id,
                Kind        = (int)p.Kind,
                Value       = p.Value,
            }).ToList();

        existing.ActionPermissions = orderType.ActionPermissions
            .Select(p => new Entities.OrderActionPermissionRecord
            {
                OrderTypeId  = orderType.Id,
                Action       = (int)p.Action,
                Kind         = (int)p.Kind,
                Value        = p.Value,
                FromStateKey = p.FromStateKey,
            }).ToList();

        existing.States = orderType.States
            .Select(s => new Entities.OrderTypeStateRecord
            {
                OrderTypeId = orderType.Id,
                Key         = s.Key,
                DisplayName = s.DisplayName,
                IsSuccess   = s.IsSuccess,
            }).ToList();

        existing.Transitions = orderType.Transitions
            .Select(x => new Entities.OrderTypeTransitionRecord
            {
                OrderTypeId   = orderType.Id,
                FromStateKey  = x.FromStateKey,
                ToStateKey    = x.ToStateKey,
                TriggerAction = x.TriggerAction.HasValue ? (int)x.TriggerAction.Value : null,
            }).ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var record = await db.OrderTypes.FindAsync([id], ct);
        if (record is not null) db.OrderTypes.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
