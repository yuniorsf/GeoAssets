using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Answers <see cref="ServiceOrderActionRequirement"/> on an <see cref="IServiceOrder"/> (XD01-364) with
/// <see cref="ServiceOrderRules"/> for the caller's <see cref="WorkflowPrincipal"/>: the organization gate, then the rule
/// chain — the type's action permissions, the creator, assignee and dispatch relationships, the roles and cross-org grants.
/// A refusal carries the verdict (<see cref="ServiceOrderAuthorizationFailureReason"/>).
/// </summary>
public sealed class ServiceOrderActionAuthorizationHandler(
    ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory)
    : AuthorizationHandler<ServiceOrderActionRequirement, IServiceOrder>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ServiceOrderActionRequirement requirement, IServiceOrder resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(resource);

        var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
        var verdict = requirement.Entry is { } entry
            ? rules.EvaluateAppend(principal, resource, entry)
            : rules.Evaluate(principal, requirement.Action, resource);

        if (verdict.Allowed)
            context.Succeed(requirement);
        else
            context.Fail(new ServiceOrderAuthorizationFailureReason(this, verdict));
    }
}
