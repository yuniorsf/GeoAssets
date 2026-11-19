using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// "May the caller take this action on this service order?" as an ASP.NET Core resource requirement (XD01-364, ADR-001
/// smell H), answered by <see cref="ServiceOrderActionAuthorizationHandler"/> with <c>ServiceOrderRules</c> — the one
/// decision point every route that acts on an <see cref="IServiceOrder"/> goes through, instead of a permission check
/// that ignores the order type's policies, the dispatch and the organization gate. Evaluate it with
/// <c>IAuthorizationService.AuthorizeAsync(user, order, requirement)</c> once the order is loaded, and answer a failure
/// with <see cref="ServiceOrderRouteAccess.Refusal(AuthorizationResult)"/>.
/// </summary>
public sealed class ServiceOrderActionRequirement : IAuthorizationRequirement
{
    /// <summary>The bare action, evaluated as <c>ServiceOrderRules.Evaluate</c> does.</summary>
    public ServiceOrderActionRequirement(OrderActionType action) => Action = action;

    /// <summary>
    /// The action-log entry the caller is about to append, evaluated as <c>ServiceOrderRules.EvaluateAppend</c> does — so a
    /// recipient of the order's dispatch moves it only along the transition its action is tagged with.
    /// </summary>
    public ServiceOrderActionRequirement(OrderActionLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Action = entry.Action;
        Entry = entry;
    }

    public OrderActionType Action { get; }

    /// <summary>The entry to append, when the requirement was built from one.</summary>
    public OrderActionLog? Entry { get; }
}
