using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// <see cref="ServiceOrderRules"/>: what sets a recipient of an order's dispatch apart from a
/// member of the order's organization and from a grantee (XD01-316).
/// </summary>
public sealed partial class ServiceOrderRules
{
    /// <summary>
    /// The reason given to a principal that may annotate an order dispatched to it and still may
    /// not change that order's targets — see <see cref="IsDispatchRecipientOnly"/>.
    /// </summary>
    public const string RecipientTargetsAreReadOnlyReason =
        "The targets of an order dispatched to you are read-only: they belong to the organization that owns the order.";

    /// <summary>
    /// Whether <paramref name="principal"/> reaches <paramref name="order"/> only because the
    /// order was dispatched to it — to its user id, a group of its, or its organization — and
    /// neither as a member of the order's organization nor through a cross-organization grant
    /// covering the order (XD01-316). Such a recipient may read the order's targets and never
    /// change them: the targets are the owning organization's assets, listed for the recipient's
    /// information. False for a principal the organization gate refuses, and for an anonymous one.
    /// </summary>
    public bool IsDispatchRecipientOnly(WorkflowPrincipal principal, IServiceOrder order)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrEmpty(principal.UserId)) return false;

        var context = new RuleEvaluationContext(principal, order, ResolveRelationship(principal, order));
        return (context.Relationship & OrderOrganizationGate.RecipientFlags) != OrderUserRelationship.None
            && !OrderOrganizationGate.PassesWithoutDispatch(
                context, _allowOrdersWithoutOrganization, _timeProvider.GetUtcNow().UtcDateTime);
    }
}
