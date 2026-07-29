using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>
/// Creates a <see cref="ServiceOrder"/> on behalf of a registered agent actor.
/// Authorization goes through the exact same <see cref="ServiceOrderRules.CanCreate"/>
/// check a human-driven caller would hit — this executor adds no privilege of its own.
/// </summary>
public sealed class CreateServiceOrderExecutor(
    IServiceOrderWriter     writer,
    ServiceOrderRules       rules,
    OrderTypeRegistry       orderTypeRegistry,
    IAgentIdentityProvider  identity,
    string                  id = nameof(CreateServiceOrderExecutor))
    : Executor<CreateServiceOrderRequest, ServiceOrderCreated>(id)
{
    public override async ValueTask<ServiceOrderCreated> HandleAsync(
        CreateServiceOrderRequest message,
        IWorkflowContext          context,
        CancellationToken         cancellationToken = default)
    {
        var principal = identity.Resolve(message.AgentId);
        var orderType = orderTypeRegistry.Get(message.OrderTypeId);

        if (!rules.CanCreate(principal, orderType))
            throw new InvalidOperationException(
                $"Agent '{message.AgentId}' is not authorized to create '{message.OrderTypeId}' orders.");

        var order = new ServiceOrder
        {
            Title       = message.Title,
            OrderTypeId = message.OrderTypeId,
            CreatedBy   = message.AgentId,
        };

        await writer.AddAsync(order, cancellationToken);

        return new ServiceOrderCreated(
            order,
            message.AgentId,
            message.DispatchTargetId,
            message.DispatchTargetType,
            message.AgentInvocationId);
    }
}
