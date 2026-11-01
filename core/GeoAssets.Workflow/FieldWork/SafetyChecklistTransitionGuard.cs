using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Real implementation of <see cref="IFieldWorkTransitionGuard.CanStartWorkAsync"/> (XD01-222):
/// true once the order's own <see cref="IServiceOrder.Attributes"/> — the crew's digital
/// checklist submission — validates against its <see cref="OrderType.AttributesSchemaJson"/>
/// (see <see cref="ConcessionaireSafetyChecklistSchemas"/>), via the existing
/// <see cref="ServiceOrderAttributeValidator"/> pipeline.
///
/// An order whose type isn't registered, or is registered with no
/// <see cref="OrderType.AttributesSchemaJson"/>, is unrestricted — same "no schema = no gate"
/// convention <see cref="ServiceOrderAttributeValidator"/> itself already uses. This is correct
/// here too: the safety gate only applies to order types that actually carry a safety-checklist
/// schema, not to every field-work order type.
///
/// <see cref="CanArriveAsync"/> is not this ticket's scope (XD01-221 owns the geofence check) —
/// always returns true here, same as <see cref="NullFieldWorkTransitionGuard"/>; compose with
/// <see cref="CompositeFieldWorkTransitionGuard"/> to enforce both gates together.
/// </summary>
public sealed class SafetyChecklistTransitionGuard(OrderTypeRegistry orderTypeRegistry) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var orderType = orderTypeRegistry.Find(order.OrderTypeId);
        if (orderType is null) return Task.FromResult(true);

        var errors = ServiceOrderAttributeValidator.Validate(orderType, order.Attributes);
        return Task.FromResult(errors.Count == 0);
    }

    /// <summary>Not this ticket's scope (XD01-246 owns completion's photo-evidence gate) — always true, same as <see cref="NullFieldWorkTransitionGuard"/>.</summary>
    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);
}
