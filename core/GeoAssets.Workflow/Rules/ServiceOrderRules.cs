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
/// Creation is gated by a separate chain of <see cref="IOrderCreationRule"/> instances
/// (no <see cref="IServiceOrder"/> exists yet at creation time), using the same
/// deny-overrides logic. Extend either chain via <see cref="AddRule"/> / <see cref="AddCreationRule"/>.
///
/// Passing an <see cref="Orders.OrderTypeRegistry"/> lets the engine also honor
/// per-order-type <see cref="OrderType.ActionPermissions"/>: when an order type defines
/// permissions for a given action, satisfying them is required and overrides the
/// built-in default rules for that action; when it defines none for that action,
/// evaluation falls through to the defaults unchanged.
///
/// Usage:
/// <code>
///   var rules = new ServiceOrderRules(orderTypeRegistry)
///       .AddRule(new MyCustomSupervisorRule())
///       .AddCreationRule(new MyCustomCreationRule());
///
///   bool canCreate = rules.CanCreate(principal, orderType);
///   var  result    = rules.Evaluate(principal, OrderActionType.Approve, order);
/// </code>
/// </summary>
public sealed class ServiceOrderRules
{
    private readonly List<IServiceOrderRule>   _rules;
    private readonly List<IOrderCreationRule>  _creationRules;

    /// <param name="orderTypeRegistry">
    /// Optional. When supplied, <see cref="Evaluate"/> also consults each order type's
    /// <see cref="OrderType.ActionPermissions"/> (see class summary). When omitted,
    /// evaluation only ever uses the built-in default rules — the same behavior as before
    /// this parameter existed.
    /// </param>
    /// <param name="roleGrants">
    /// Optional. Overrides <see cref="RoleBasedActionRule.DefaultRoleGrants"/> — which
    /// actions each role name grants — without needing to add a custom
    /// <see cref="IServiceOrderRule"/> or edit this class. Omit to keep the defaults.
    /// </param>
    /// <param name="unrestrictedRoles">
    /// Optional. Overrides <see cref="RoleBasedActionRule.DefaultUnrestrictedRoles"/> —
    /// role names granted every action. Omit to keep the default ("Administrator").
    /// </param>
    /// <param name="recipientRoleGrants">
    /// Optional. Actions a dispatch recipient (direct/group/org) may additionally perform
    /// when they also hold the paired role — see <see cref="DispatchRecipientRule"/>. Unlike
    /// <paramref name="roleGrants"/> (global, applies to every order), this only applies to
    /// orders actually dispatched to the principal. Omit to keep the default (none — a
    /// recipient without a configured role grant still only gets View/Annotate/Accept).
    /// </param>
    public ServiceOrderRules(
        OrderTypeRegistry? orderTypeRegistry = null,
        IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>>? roleGrants = null,
        IReadOnlySet<string>? unrestrictedRoles = null,
        IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>>? recipientRoleGrants = null)
    {
        _rules =
        [
            new CreatorRule(orderTypeRegistry),
            new AssigneeRule(),
            new DispatchRecipientRule(recipientRoleGrants),
            new OrderTypeActionPermissionRule(orderTypeRegistry),
            new RoleBasedActionRule(roleGrants, unrestrictedRoles),
            new CrossOrgGrantRule(),
        ];

        _creationRules =
        [
            new CreationPolicyRule(),
        ];
    }

    // ── Fluent configuration ──────────────────────────────────────────────────

    /// <summary>Appends a custom rule to the action-evaluation chain.</summary>
    public ServiceOrderRules AddRule(IServiceOrderRule rule)
    {
        _rules.Add(rule);
        return this;
    }

    /// <summary>Appends a custom rule to the creation-evaluation chain.</summary>
    public ServiceOrderRules AddCreationRule(IOrderCreationRule rule)
    {
        _creationRules.Add(rule);
        return this;
    }

    // ── Creation gate ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="principal"/> is allowed to create an order
    /// of the given <paramref name="orderType"/>, using the same deny-overrides logic
    /// as <see cref="Evaluate"/> over the creation-rule chain (see <see cref="AddCreationRule"/>).
    /// The built-in <see cref="CreationPolicyRule"/> grants when the principal satisfies
    /// at least one of <see cref="OrderType.CreationPolicies"/> (or when none are defined),
    /// and otherwise abstains — leaving room for a custom creation rule to grant access
    /// through a different path entirely.
    /// </summary>
    public bool CanCreate(WorkflowPrincipal principal, OrderType orderType)
    {
        if (string.IsNullOrEmpty(principal.UserId)) return false;

        bool anyAllow = false;
        foreach (var rule in _creationRules)
        {
            var verdict = rule.Evaluate(principal, orderType);
            if (verdict == false)
                return false;
            if (verdict == true)
                anyAllow = true;
        }

        return anyAllow;
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
/// The creator of an order may always view it, annotate it, and cancel it while a
/// Cancel-triggering transition still exists from its current state (see
/// <see cref="ServiceOrderTransitions.HasTransitionFor"/> — for order types with no
/// custom workflow graph this is exactly "still Draft or Pending", the historical
/// behavior; for a custom graph it's derived from that type's own transitions).
/// </summary>
file sealed class CreatorRule(OrderTypeRegistry? orderTypeRegistry = null) : IServiceOrderRule
{
    public string Name => "CreatorRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if (!ctx.Is(OrderUserRelationship.Creator)) return null;

        return action switch
        {
            OrderActionType.View     => true,
            OrderActionType.Annotate => true,
            OrderActionType.Cancel   => ServiceOrderTransitions.HasTransitionFor(
                                            orderTypeRegistry?.Find(ctx.Order.OrderTypeId),
                                            ctx.Order.Status,
                                            OrderActionType.Cancel)
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
/// group or organization, may unconditionally view, annotate, and accept the order —
/// "accept" being how any qualifying member of a dispatched-to group/org claims an
/// order that wasn't addressed to a pre-named individual.
///
/// Broader actions (<see cref="OrderActionType.Assign"/>, <see cref="OrderActionType.Dispatch"/>,
/// <see cref="OrderActionType.Execute"/>, <see cref="OrderActionType.Reject"/>, or any other
/// action a host chooses to configure) require *also* holding a paired role — set via
/// <paramref name="roleGrants"/> — expressing "(is a recipient of this dispatch) AND (has role X)."
/// This is deliberately narrower than <see cref="RoleBasedActionRule"/>'s global role grants:
/// a role listed here only unlocks the action on orders actually dispatched to the principal,
/// not on every order in the system.
/// </summary>
file sealed class DispatchRecipientRule(
    IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>>? roleGrants = null) : IServiceOrderRule
{
    public string Name => "DispatchRecipientRule";

    private static readonly OrderUserRelationship _recipientFlags =
        OrderUserRelationship.DirectRecipient |
        OrderUserRelationship.GroupMember     |
        OrderUserRelationship.OrgMember;

    private readonly IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>> _roleGrants =
        roleGrants ?? new Dictionary<string, IReadOnlySet<OrderActionType>>();

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if ((ctx.Relationship & _recipientFlags) == 0) return null;

        switch (action)
        {
            case OrderActionType.View:
            case OrderActionType.Annotate:
            case OrderActionType.Accept:
                return true;
        }

        foreach (var (role, actions) in _roleGrants)
        {
            if (actions.Contains(action) && ctx.Principal.HasRole(role))
                return true;
        }

        return null;
    }
}

/// <summary>
/// Default role-based action grants. Mirrors common supervisory hierarchies, but the
/// mapping is data (not a hardcoded switch), so a host can narrow, extend, or replace
/// it entirely via the <c>roleGrants</c>/<c>unrestrictedRoles</c> parameters on
/// <see cref="ServiceOrderRules"/>'s constructor — without editing this class — e.g. to
/// stop granting Supervisors <see cref="OrderActionType.Reject"/>, or to add a new tier.
///
/// Roles in <paramref name="unrestrictedRoles"/> (default: "Administrator") grant every
/// action. Every other entry in <paramref name="roleGrants"/> (default: "Supervisor" →
/// View/Approve/Reject/Assign/Dispatch/Cancel/Annotate) grants only its listed actions.
/// A principal with no matching role abstains, same as every other built-in rule.
/// </summary>
file sealed class RoleBasedActionRule(
    IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>>? roleGrants = null,
    IReadOnlySet<string>? unrestrictedRoles = null) : IServiceOrderRule
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>> DefaultRoleGrants =
        new Dictionary<string, IReadOnlySet<OrderActionType>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Supervisor"] = new HashSet<OrderActionType>
            {
                OrderActionType.View, OrderActionType.Approve, OrderActionType.Reject,
                OrderActionType.Assign, OrderActionType.Dispatch, OrderActionType.Cancel,
                OrderActionType.Annotate,
            },
        };

    public static readonly IReadOnlySet<string> DefaultUnrestrictedRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Administrator" };

    private readonly IReadOnlyDictionary<string, IReadOnlySet<OrderActionType>> _roleGrants =
        roleGrants ?? DefaultRoleGrants;
    private readonly IReadOnlySet<string> _unrestrictedRoles =
        unrestrictedRoles ?? DefaultUnrestrictedRoles;

    public string Name => "RoleBasedActionRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        if (_unrestrictedRoles.Any(ctx.Principal.HasRole))
            return true;

        foreach (var (role, actions) in _roleGrants)
        {
            if (actions.Contains(action) && ctx.Principal.HasRole(role))
                return true;
        }

        return null;
    }
}

/// <summary>
/// Allow-contributor for cross-organization access (XD01-22): grants the action when the
/// principal's own organization holds an active <see cref="WorkflowOrgGrant"/> — pre-resolved
/// onto <see cref="WorkflowPrincipal.OrgGrants"/> — covering the order's owning organization
/// (<see cref="IServiceOrder.OrganizationId"/>). Matches on resource org, optional resource
/// type (null = any), the action's permission code (<c>"serviceorders:{action}"</c>, lowercase
/// — e.g. <c>"serviceorders:complete"</c>, matching the format decided when
/// <c>OrganizationGrant</c> was designed), and an optional required role.
///
/// Same pattern as every other built-in rule: abstains (returns <c>null</c>) rather than
/// denying when no grant applies, so same-org access via the existing rules (Creator/
/// Assignee/DispatchRecipient/RoleBased) is entirely unaffected — this only ever *adds* an
/// allow path, never a new deny condition.
/// </summary>
file sealed class CrossOrgGrantRule : IServiceOrderRule
{
    public string Name => "CrossOrgGrantRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        var actionCode = $"serviceorders:{action.ToString().ToLowerInvariant()}";
        var resourceOrganizationId = ctx.Order.OrganizationId.ToString();

        var hasGrant = ctx.Principal.OrgGrants.Any(g =>
            g.ResourceOrganizationId.Equals(resourceOrganizationId, StringComparison.OrdinalIgnoreCase) &&
            (g.ResourceType is null || g.ResourceType.Equals(nameof(Orders.ServiceOrder), StringComparison.OrdinalIgnoreCase)) &&
            g.AllowedActions.Contains(actionCode, StringComparer.OrdinalIgnoreCase) &&
            (g.RequiredRole is null || ctx.Principal.HasRole(g.RequiredRole)));

        return hasGrant ? true : null;
    }
}

/// <summary>
/// Consults the current order's <see cref="OrderType.ActionPermissions"/> (resolved via
/// the optional <see cref="OrderTypeRegistry"/> passed to <see cref="ServiceOrderRules"/>).
///
/// When the order's type defines one or more permission entries for the action being
/// evaluated, satisfying at least one is required — returning <c>false</c> here overrides
/// any grant from the built-in default rules, since a single explicit DENY wins the whole
/// evaluation. When the type defines no entries for that action (or no registry was
/// supplied, or the type isn't registered), this rule abstains and evaluation falls
/// through to the defaults unchanged.
/// </summary>
file sealed class OrderTypeActionPermissionRule(OrderTypeRegistry? registry) : IServiceOrderRule
{
    public string Name => "OrderTypeActionPermissionRule";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx)
    {
        var orderType = registry?.Find(ctx.Order.OrderTypeId);
        if (orderType is null) return null;

        var permissions = orderType.ActionPermissions
            .Where(p => p.Action == action && (p.FromStateKey is null || p.FromStateKey == ctx.Order.Status))
            .ToList();
        if (permissions.Count == 0) return null;

        return permissions.Any(p => ServiceOrderRules.SatisfiesPolicy(ctx.Principal, p.Kind, p.Value));
    }
}

// ── Default built-in creation rule ─────────────────────────────────────────────

/// <summary>
/// Grants creation when the principal satisfies at least one of
/// <see cref="OrderType.CreationPolicies"/> (any-match), or when none are defined
/// (creation is then open to all authenticated users). Abstains — rather than
/// explicitly denying — when policies are defined but unsatisfied, so a custom
/// <see cref="IOrderCreationRule"/> can still grant access through a different path.
/// </summary>
file sealed class CreationPolicyRule : IOrderCreationRule
{
    public string Name => "CreationPolicyRule";

    public bool? Evaluate(WorkflowPrincipal principal, OrderType orderType)
    {
        var policies = orderType.CreationPolicies;
        if (policies.Count == 0) return true;

        return policies.Any(p => ServiceOrderRules.SatisfiesPolicy(principal, p.Kind, p.Value))
            ? true
            : null;
    }
}
