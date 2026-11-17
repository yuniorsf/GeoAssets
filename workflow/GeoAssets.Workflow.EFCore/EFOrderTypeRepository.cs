using GeoAssets.Workflow.Orders;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IOrderTypeRepository"/>. A type's
/// <see cref="OrderType.RecipientActions"/> live in <c>OrderTypeRecipientActions</c>, keyed by the
/// type's id with no foreign key (XD01-336, see <see cref="Entities.OrderTypeRecipientActionRecord"/>):
/// written and removed here alongside the type, read back by the same id.
/// </summary>
public sealed class EFOrderTypeRepository(ServiceOrderDbContext db) : IOrderTypeRepository
{
    public async Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var record = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);

        if (record is null) return null;

        var recipientActions = await db.OrderTypeRecipientActions
            .Where(a => a.OrderTypeId == record.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        return OrderTypeMapper.ToDomain(record, recipientActions);
    }

    public async Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .OrderBy(t => t.DisplayName)
            .ToListAsync(ct).ConfigureAwait(false);

        var ids = records.Select(r => r.Id).ToList();
        var recipientActions = (await db.OrderTypeRecipientActions
                .Where(a => ids.Contains(a.OrderTypeId))
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(a => a.OrderTypeId, StringComparer.Ordinal);

        return records.Select(r => OrderTypeMapper.ToDomain(r, recipientActions[r.Id])).ToList();
    }

    public async Task AddAsync(OrderType orderType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        await db.OrderTypes.AddAsync(OrderTypeMapper.ToRecord(orderType), ct).ConfigureAwait(false);
        await ReplaceRecipientActionsAsync(orderType, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OrderType orderType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        var existing = await db.OrderTypes
            .Include(t => t.CreationPolicies)
            .Include(t => t.ActionPermissions)
            .Include(t => t.States)
            .Include(t => t.Transitions)
            .FirstOrDefaultAsync(t => t.Id == orderType.Id, ct).ConfigureAwait(false);

        if (existing is null) return;

        existing.DisplayName     = orderType.DisplayName;
        existing.Description     = orderType.Description;
        existing.InitialStateKey = orderType.InitialStateKey;
        existing.AttributesSchemaJson = orderType.AttributesSchemaJson;
        existing.TargetPolicyJson = OrderTargetPolicyMapper.ToJson(orderType.TargetPolicy);
        existing.RecipientAccess = (int)orderType.RecipientAccess;
        await ReplaceRecipientActionsAsync(orderType, ct).ConfigureAwait(false);

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
        var record = await db.OrderTypes.FindAsync([id], ct).ConfigureAwait(false);
        if (record is null) return;

        db.OrderTypes.Remove(record);
        db.OrderTypeRecipientActions.RemoveRange(
            await db.OrderTypeRecipientActions.Where(a => a.OrderTypeId == id).ToListAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Puts <paramref name="orderType"/>'s recipient actions in place of whatever rows its id had —
    /// including rows a migration seeded for a type of that id registered only in code.
    /// </summary>
    private async Task ReplaceRecipientActionsAsync(OrderType orderType, CancellationToken ct)
    {
        db.OrderTypeRecipientActions.RemoveRange(
            await db.OrderTypeRecipientActions.Where(a => a.OrderTypeId == orderType.Id).ToListAsync(ct).ConfigureAwait(false));
        await db.OrderTypeRecipientActions.AddRangeAsync(OrderTypeMapper.ToRecipientActionRecords(orderType), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
