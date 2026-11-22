using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Allow-contributor for cross-organization access (XD01-22): grants the action when the
/// principal's own organization holds an active <see cref="WorkflowOrgGrant"/> — pre-resolved
/// onto <see cref="WorkflowPrincipal.OrgGrants"/> — covering the order's owning organization
/// (<see cref="IServiceOrder.OrganizationId"/>). Matches on resource org, optional resource
/// type (null = any), the action's permission code (<c>"serviceorders:{action}"</c>, lowercase
/// — e.g. <c>"serviceorders:complete"</c>, matching the format decided when
/// <c>OrganizationGrant</c> was designed), an optional required role, and — when the grant
/// names one (XD01-250) — the order's own <see cref="GeoAssets.Core.Interfaces.IScopeableResource.ScopeId"/>, by exact,
/// case-sensitive match per that interface's own contract. A grant with no
/// <see cref="WorkflowOrgGrant.ScopeId"/> stays ungated within <c>ResourceType</c>, the
/// original behavior; a grant that does name one never matches an order whose own ScopeId is
/// null (there is no subset to match against) or implements no scoping at all. A grant may also
/// be narrowed to order types and require a scoped role (XD01-326) — see <see cref="CrossOrgGrantMatcher"/>.
///
/// Same pattern as every other built-in rule: abstains (returns <c>null</c>) rather than
/// denying when no grant applies, so same-org access via the existing rules (Creator/
/// Assignee/DispatchRecipient/RoleBased) is entirely unaffected — this only ever *adds* an
/// allow path, never a new deny condition. The matching itself is <see cref="CrossOrgGrantMatcher"/>,
/// which the organization gate (XD01-332) shares.
/// </summary>
internal sealed class CrossOrgGrantRule(TimeProvider timeProvider) : IServiceOrderRule
{
    public string Name => "CrossOrgGrantRule";

    public bool? Evaluate(OrderAction action, RuleEvaluationContext ctx) =>
        CrossOrgGrantMatcher.CoversAction(ctx.Principal, action, ctx.Order, timeProvider.GetUtcNow().UtcDateTime) ? true : null;
}
