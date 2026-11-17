using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// <see cref="ServiceOrderRules"/>: what sets a recipient of an order's dispatch apart from a
/// member of the order's organization and from a grantee (XD01-316), and how much of the order
/// its type lets that recipient change (XD01-333).
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
    /// The reason given to a recipient of an order's dispatch whose order type does not let a
    /// recipient change the order (anything below <see cref="OrderRecipientAccess.Edit"/>).
    /// </summary>
    public const string RecipientOrderIsReadOnlyReason =
        "This order was dispatched to you, and its type does not let a recipient change it.";

    /// <summary>
    /// The reason given to a recipient allowed to edit an order dispatched to it whose update
    /// changes what the order is — see <see cref="ServiceOrderRecipientEdit.FixedFieldsChanged"/>.
    /// </summary>
    public const string RecipientOrderFieldsAreFixedReason =
        "This order was dispatched to you: you may edit its content, not its type, status, assignee, completion, parent order, scope or recipient.";

    /// <summary>
    /// Whether <paramref name="principal"/> reaches <paramref name="order"/> only because the
    /// order was dispatched to it — to its user id, a group of its, or its organization — and
    /// neither as a member of the order's organization nor through a cross-organization grant
    /// covering the order (XD01-316). What such a recipient may change is bounded by the order
    /// type's <see cref="OrderType.RecipientAccess"/> (XD01-333). False for a principal the
    /// organization gate refuses, and for an anonymous one.
    /// </summary>
    public bool IsDispatchRecipientOnly(WorkflowPrincipal principal, IServiceOrder order)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrEmpty(principal.UserId)) return false;

        return IsRecipientOnly(new RuleEvaluationContext(principal, order, ResolveRelationship(principal, order)));
    }

    /// <summary>
    /// The <see cref="OrderType.RecipientAccess"/> of <paramref name="order"/>'s type, or
    /// <see cref="OrderRecipientAccess.Inform"/> when no registry was given, the type is not
    /// registered, or it holds a value outside the enumeration — never more than the type says.
    /// </summary>
    public OrderRecipientAccess RecipientAccessOf(IServiceOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var access = _orderTypeRegistry?.Find(order.OrderTypeId)?.RecipientAccess ?? OrderRecipientAccess.Inform;
        return Enum.IsDefined(access) ? access : OrderRecipientAccess.Inform;
    }

    /// <summary>
    /// Whether <paramref name="principal"/> may make a change of kind <paramref name="write"/> to
    /// the targets of <paramref name="order"/>. The rule chain must allow it
    /// <see cref="OrderActionType.Annotate"/> (refused as <see cref="Evaluate"/> refuses, the
    /// organization gate included); then, for a principal that reaches the order only as a
    /// recipient of its dispatch, recording work needs a type of at least
    /// <see cref="OrderRecipientAccess.Execute"/>, and adding or removing targets is never
    /// allowed — the targets are the owning organization's assets. Refused with
    /// <see cref="RecipientTargetsAreReadOnlyReason"/> and
    /// <see cref="RuleDenialCode.DeniedByRecipientAccess"/>. Members and grantees: exactly
    /// <see cref="Evaluate"/> for <see cref="OrderActionType.Annotate"/>.
    /// </summary>
    public RuleEvaluationResult EvaluateTargetWrite(WorkflowPrincipal principal, IServiceOrder order, OrderTargetWrite write)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrEmpty(principal.UserId))
            return Anonymous(OrderActionType.Annotate);

        var context = new RuleEvaluationContext(principal, order, ResolveRelationship(principal, order));
        if (!OrderOrganizationGate.Passes(context, _allowOrdersWithoutOrganization, _timeProvider.GetUtcNow().UtcDateTime))
        {
            return RuleEvaluationResult.Deny(
                OrderActionType.Annotate, "The order is outside the principal's organization.", RuleDenialCode.OutsideOrganization);
        }

        var verdict = EvaluateChain(OrderActionType.Annotate, context);
        if (!verdict.Allowed || !IsRecipientOnly(context)) return verdict;

        return write == OrderTargetWrite.RecordWork && RecipientAccessOf(order) >= OrderRecipientAccess.Execute
            ? verdict
            : RuleEvaluationResult.Deny(
                OrderActionType.Annotate, RecipientTargetsAreReadOnlyReason, RuleDenialCode.DeniedByRecipientAccess);
    }

    /// <summary>
    /// Whether <paramref name="principal"/> may replace <paramref name="stored"/> with
    /// <paramref name="replacement"/> through the whole-order update: <see cref="Evaluate"/> for
    /// <see cref="OrderActionType.Annotate"/> — so a recipient needs an
    /// <see cref="OrderRecipientAccess.Edit"/> type — and then, for a principal that reaches the
    /// order only as a recipient of its dispatch, a replacement that leaves every field of
    /// <see cref="ServiceOrderRecipientEdit.FixedFieldsChanged"/> as it is (refused with
    /// <see cref="RecipientOrderFieldsAreFixedReason"/>). Members and grantees: exactly
    /// <see cref="Evaluate"/>.
    /// </summary>
    public RuleEvaluationResult EvaluateReplace(WorkflowPrincipal principal, IServiceOrder stored, IServiceOrder replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var verdict = Evaluate(principal, OrderActionType.Annotate, stored);
        if (!verdict.Allowed || !IsDispatchRecipientOnly(principal, stored)) return verdict;

        return ServiceOrderRecipientEdit.FixedFieldsChanged(stored, replacement).Count == 0
            ? verdict
            : RuleEvaluationResult.Deny(
                OrderActionType.Annotate, RecipientOrderFieldsAreFixedReason, RuleDenialCode.DeniedByRecipientAccess);
    }

    /// <summary>
    /// <paramref name="verdict"/> of the rule chain, held to the order type's recipient access when
    /// the principal reaches the order only as a recipient of its dispatch: below
    /// <see cref="OrderRecipientAccess.Edit"/> such a recipient keeps
    /// <see cref="OrderActionType.View"/> and nothing else.
    /// </summary>
    private RuleEvaluationResult LimitToRecipientAccess(RuleEvaluationResult verdict, RuleEvaluationContext context)
    {
        if (!verdict.Allowed || verdict.Action == OrderActionType.View || !IsRecipientOnly(context)) return verdict;

        return RecipientAccessOf(context.Order) == OrderRecipientAccess.Edit
            ? verdict
            : RuleEvaluationResult.Deny(verdict.Action, RecipientOrderIsReadOnlyReason, RuleDenialCode.DeniedByRecipientAccess);
    }

    private bool IsRecipientOnly(RuleEvaluationContext context) =>
        (context.Relationship & OrderOrganizationGate.RecipientFlags) != OrderUserRelationship.None
        && !OrderOrganizationGate.PassesWithoutDispatch(
            context, _allowOrdersWithoutOrganization, _timeProvider.GetUtcNow().UtcDateTime);
}
