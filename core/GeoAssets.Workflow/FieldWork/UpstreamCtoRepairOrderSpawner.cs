using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Builds the upstream CTO repair <see cref="ServiceOrder"/> XD01-274's AC calls for — "Below
/// −22.0 dBm at CTO auto-creates an upstream repair work order" — from a failing
/// <see cref="OpticalBudgetVerificationResult"/>. Pure domain logic, no infrastructure
/// dependency of its own, same division of responsibility as
/// <see cref="WaterPavingSubOrderSpawner"/>: the pure verification lives in
/// <see cref="OpticalBudgetVerificationService"/> (<c>GeoAssets.Core</c>, no <see cref="ServiceOrder"/>
/// dependency); this type only reacts to its result to decide whether/what to spawn.
/// </summary>
public static class UpstreamCtoRepairOrderSpawner
{
    /// <summary>
    /// Returns the upstream repair child order when <paramref name="verificationResult"/>
    /// requires one (<see cref="OpticalBudgetVerificationResult.RequiresUpstreamCtoRepair"/>),
    /// else null. Does not mutate or transition <paramref name="dropOrder"/> — wiring this into
    /// the FTTH drop-install workflow's own transition/persistence is a later ticket's concern.
    /// </summary>
    public static ServiceOrder? BuildIfNeeded(
        OpticalBudgetVerificationResult verificationResult,
        ServiceOrder dropOrder,
        string ctoFeatureId,
        OrderType repairOrderType,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(verificationResult);
        ArgumentNullException.ThrowIfNull(dropOrder);
        ArgumentNullException.ThrowIfNull(ctoFeatureId);
        ArgumentNullException.ThrowIfNull(repairOrderType);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!verificationResult.RequiresUpstreamCtoRepair) return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ctoFeatureId"] = ctoFeatureId,
            ["opmViolationDetail"] = verificationResult.ViolationDetail ?? string.Empty,
        };

        // Same scope-id inheritance as WaterPavingSubOrderSpawner (XD01-252) — an
        // OrganizationGrant scoped to the parent drop order's município/subset must also
        // authorize this auto-spawned repair order.
        if (((IScopeableResource)dropOrder).ScopeId is { } parentScopeId)
            attributes["scopeId"] = parentScopeId;

        return new ServiceOrder
        {
            Title = $"Upstream CTO repair — {dropOrder.Title}",
            OrderTypeId = repairOrderType.Id,
            ParentOrderId = dropOrder.Id,
            OrganizationId = dropOrder.OrganizationId,
            CreatedBy = dropOrder.CreatedBy,
            CreatedAt = now,
            Status = repairOrderType.InitialStateKey ?? ServiceOrderStatus.Draft,
            Attributes = attributes,
        };
    }
}
