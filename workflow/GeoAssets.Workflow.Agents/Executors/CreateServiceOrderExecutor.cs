using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>
/// Creates a <see cref="ServiceOrder"/> on behalf of a registered agent actor.
/// Authorization goes through the exact same <see cref="ServiceOrderRules.CanCreate"/>
/// check a human-driven caller would hit — this executor adds no privilege of its own.
/// </summary>
public sealed class CreateServiceOrderExecutor(
    IServiceOrderWriter                    writer,
    ServiceOrderRules                      rules,
    OrderTypeRegistry                      orderTypeRegistry,
    IAgentIdentityProvider                 identity,
    GeoAssetsActivitySource                tracer,
    ILogger<CreateServiceOrderExecutor>    logger,
    string                                  id = nameof(CreateServiceOrderExecutor))
    : Executor<CreateServiceOrderRequest, ServiceOrderCreated>(id)
{
    public override async ValueTask<ServiceOrderCreated> HandleAsync(
        CreateServiceOrderRequest message,
        IWorkflowContext          context,
        CancellationToken         cancellationToken = default)
    {
        // Generated up front (matches ServiceOrder.Id's own default) so the span can carry
        // order.id from the start, covering the authorization check too.
        var orderId = Guid.NewGuid().ToString();
        using var span = tracer.StartAgentActivity("Create", orderId, message.AgentId, message.AgentInvocationId);

        try
        {
            var principal = identity.Resolve(message.AgentId);
            var orderType = orderTypeRegistry.Get(message.OrderTypeId);

            var allowed = rules.CanCreate(principal, orderType);
            span?.AddTag("decision.allowed", allowed);

            if (!allowed)
            {
                logger.LogWarning(
                    "Agent {AgentId} denied Create on order type {OrderTypeId}",
                    message.AgentId, message.OrderTypeId);
                throw new InvalidOperationException(
                    $"Agent '{message.AgentId}' is not authorized to create '{message.OrderTypeId}' orders.");
            }

            var order = new ServiceOrder
            {
                Id          = orderId,
                Title       = message.Title,
                OrderTypeId = message.OrderTypeId,
                CreatedBy   = message.AgentId,
                Status      = orderType.InitialStateKey ?? ServiceOrderStatus.Draft,
            };

            await writer.AddAsync(order, cancellationToken);

            logger.LogInformation(
                "Agent {AgentId} created order {OrderId} of type {OrderTypeId} in status {Status}",
                message.AgentId, order.Id, order.OrderTypeId, order.Status);

            return new ServiceOrderCreated(
                order,
                message.AgentId,
                message.DispatchTargetId,
                message.DispatchTargetType,
                message.AgentInvocationId);
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(span, ex);
            // The denial above already logged a warning with its own reason — avoid a
            // second, misleadingly "unexpected error" log line for the same event.
            if (ex is not InvalidOperationException)
                logger.LogError(ex,
                    "Agent {AgentId} failed to create order of type {OrderTypeId}",
                    message.AgentId, message.OrderTypeId);
            throw;
        }
    }
}
