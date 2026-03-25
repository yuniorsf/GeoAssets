using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Central authorization engine for service order actions.
///
/// Evaluates a chain of <see cref="IServiceOrderRule"/> instances using deny-overrides logic:
///   • Any explicit DENY wins over all ALLOWs.
///   • At least one explicit ALLOW with no DENYs → granted.
///   • All rules abstain → denied by default (fail-closed).
///
/// Comes with built-in default rules. Extend by calling <see cref="AddRule"/>.
///
/// Usage:
/// <code>
///   var rules = new ServiceOrderRules()
///       .AddRule(new MyCustomSupervisorRule());
///
///   bool canCreate = rules.CanCreate(principal, orderType);
///   var  result    = rules.Evaluate(principal, OrderActionType.Approve, order);
/// </code>
/// </summary>
public sealed class ServiceOrderRules
{
    private readonly List<IServiceOrderRule> _rules;

    public ServiceOrderRules()
    {
        _rules =
        [
            new CreatorRule(),
            new AssigneeRule(),
            new DispatchRecipientRule(),
            new RoleBasedActionRule(),
        ];
    }

    // ── Fluent configuration ──────────────────────────────────────────────────

    /// <summary>Appends a custom rule to the evaluation chain.</summary>
    public ServiceOrderRules AddRule(IServiceOrderRule rule)
    {
        _rules.Add(rule);
        return this;
    }

    // ── Creation gate ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="principal"/> is allowed to create an order
    /// of the given <paramref name="orderType"/>.
    ///
    /// A user passes if they satisfy AT LEAST ONE creation policy.
    /// When no policies are defined, creation is open to all authenticated users.
    /// </summary>
    public bool CanCreate(WorkflowPrincipal principal, OrderType orderType)
    {
        if (string.IsNullOrEmpty(principal.UserId)) return false;

        var policies = orderType.CreationPolicies;
        if (policies.Count == 0) return true;

        return policies.Any(p => SatisfiesPolicy(principal, p.Kind, p.Value));
    }

    // ── View gate ─────────────────────────────────────────────────────────────

    /// <summary>Returns true if the principal can view the order.</summary>
    public bool CanView(WorkflowPrincipal principal, IServiceOrder order)
        => Evaluate(principal, OrderActionType.View, order).Allowed;

    // ── Generic action gate ───────────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether <paramref name="principal"/> may perform
    /// <paramref name="action"/> on <paramref name="order"/>.
    /// </summary>
    public RuleEvaluationResult Evaluate(
        WorkflowPrincipal principal,
        OrderActionType   action,
        IServiceOrder     order)
    {
        if (string.IsNullOrEmpty(principal.UserId))
            return RuleEvaluationResult.Deny(action, "Anonymous users cannot act on orders.");

        var relationship = ResolveRelationship(principal, order);
        var context      = new RuleEvaluationContext(principal, order, relationship);

        bool anyAllow = false;
        foreach (var rule in _rules)
        {
            var verdict = rule.Evaluate(action, context);
            if (verdict == false)
                return RuleEvaluationResult.Deny(action, $"Denied by rule '{rule.Name}'.");
            if (verdict == true)
                anyAllow = true;
        }

        return anyAllow
            ? RuleEvaluationResult.Allow(action, "Allowed by evaluation chain.")
            : RuleEvaluationResult.Deny(action, "No rule granted this action.");
    }

    // ── Relationship resolver ─────────────────────────────────────────────────

    /// <summary>
    /// Computes the flags describing how the principal relates to the order.
    /// All flags that apply are OR-ed together.
    /// </summary>
    public static OrderUserRelationship ResolveRelationship(
        WorkflowPrincipal principal,
        IServiceOrder     order)
    {
        var rel = OrderUserRelationship.None;

        if (order.CreatedBy == principal.UserId)
            rel |= OrderUserRelationship.Creator;

        if (order.AssignedTo == principal.UserId)
            rel |= OrderUserRelationship.Assignee;

        foreach (var dispatch in order.Dispatches)
        {
            switch (dispatch.TargetType)
            {
                case DispatchTargetType.User when dispatch.TargetId == principal.UserId:
                    rel |= OrderUserRelationship.DirectRecipient;
                    break;

                case DispatchTargetType.Group when principal.BelongsToGroup(dispatch.TargetId):
                    rel |= OrderUserRelationship.GroupMember;
                    break;

                case DispatchTargetType.Organization when principal.BelongsToOrganization(dispatch.TargetId):
                    rel |= OrderUserRelationship.OrgMember;
                    break;
            }

            if (dispatch.DispatchedBy == principal.UserId)
                rel |= OrderUserRelationship.Dispatcher;
        }

        return rel;
    }

    // ── Policy matcher ────────────────────────────────────────────────────────

    internal static bool SatisfiesPolicy(WorkflowPrincipal p, PolicyKind kind, string value) => kind switch
    {
        PolicyKind.Role         => p.HasRole(value),
        PolicyKind.Permission   => p.HasPermission(value),
        PolicyKind.Group        => p.BelongsToGroup(value),
        PolicyKind.Organization => p.BelongsToOrganization(value),
        _                       => false,
    };
}

// ── Default built-in rules ─────────────────────────────────────────────────────

/// <summary>
/// The creator of an order may always view it, annotate it, and cancel it
/// while it is still in Draft or Pending state.
/// </summary>
file sealed class CreatorRule : IServiceOrderRule
{
    public string Name => "CreatorRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if (!ctx.Is(OrderUserRelationship.Creator)) return null;

        return action switch
        {
            OrderActionType.View     => true,
            OrderActionType.Annotate => true,
            OrderActionType.Cancel   => ctx.Order.Status is
                                        ServiceOrderStatus.Draft or
                                        ServiceOrderStatus.Pending
                                        ? true : null,
            _ => null,
        };
    }
}

/// <summary>
/// The user directly assigned to an order may view it, execute it, and complete it.
/// </summary>
file sealed class AssigneeRule : IServiceOrderRule
{
    public string Name => "AssigneeRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if (!ctx.Is(OrderUserRelationship.Assignee)) return null;

        return action switch
        {
            OrderActionType.View     => true,
            OrderActionType.Execute  => true,
            OrderActionType.Complete => true,
            OrderActionType.Annotate => true,
            _ => null,
        };
    }
}

/// <summary>
/// Users who are direct recipients of a dispatch, or members of a dispatched-to
/// group or organization, may view and annotate the order.
/// </summary>
file sealed class DispatchRecipientRule : IServiceOrderRule
{
    public string Name => "DispatchRecipientRule";

    private static readonly OrderUserRelationship _recipientFlags =
        OrderUserRelationship.DirectRecipient |
        OrderUserRelationship.GroupMember     |
        OrderUserRelationship.OrgMember;

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if ((ctx.Relationship & _recipientFlags) == 0) return null;

        return action switch
        {
            OrderActionType.View     => true,
            OrderActionType.Annotate => true,
            _ => null,
        };
    }
}

/// <summary>
/// Default role-based action grants. These mirror common supervisory hierarchies
/// but can be overridden by adding earlier rules with explicit denies.
///
/// Supervisors: Approve, Reject, Assign, Dispatch, Cancel.
/// Administrators: all actions.
/// </summary>
file sealed class RoleBasedActionRule : IServiceOrderRule
{
    public string Name => "RoleBasedActionRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if (ctx.Principal.HasRole("Administrator"))
            return true;

        if (ctx.Principal.HasRole("Supervisor"))
            return action switch
            {
                OrderActionType.View     => true,
                OrderActionType.Approve  => true,
                OrderActionType.Reject   => true,
                OrderActionType.Assign   => true,
                OrderActionType.Dispatch => true,
                OrderActionType.Cancel   => true,
                OrderActionType.Annotate => true,
                _ => null,
            };

        return null;
    }
}
