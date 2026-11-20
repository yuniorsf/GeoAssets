using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Answers <see cref="ServiceOrderCreateRequirement"/> (XD01-364) with <see cref="ServiceOrderRules.CanCreate"/> over the
/// registered type's creation policies. A type this host does not register cannot be created.
/// </summary>
public sealed class ServiceOrderCreateAuthorizationHandler(
    ServiceOrderRules rules, OrderTypeRegistry orderTypes, ServerWorkflowPrincipalFactory principalFactory)
    : AuthorizationHandler<ServiceOrderCreateRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ServiceOrderCreateRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (orderTypes.Find(requirement.OrderTypeId) is not { } orderType)
        {
            context.Fail(new ServiceOrderAuthorizationFailureReason(
                this, $"Order type '{requirement.OrderTypeId}' is not registered."));
            return;
        }

        var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
        if (rules.CanCreate(principal, orderType))
            context.Succeed(requirement);
        else
            context.Fail(new ServiceOrderAuthorizationFailureReason(
                this, $"No creation policy of order type '{orderType.Id}' admits the caller."));
    }
}
