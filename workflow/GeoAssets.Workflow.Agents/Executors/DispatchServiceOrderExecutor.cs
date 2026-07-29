using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>
/// Dispatches and activates (Draft → Pending) a just-created order, on behalf of the same
/// agent actor that created it — but only if <see cref="ServiceOrderRules.Evaluate"/> grants
/// that actor the <see cref="OrderActionType.Dispatch"/> action for this order.
///
/// When it does not (e.g. this <c>OrderType</c>'s configuration withholds Dispatch from the
/// agent's role), this executor leaves the order in <see cref="ServiceOrderStatus.Draft"/>
/// untouched — the same order a human can pick up later through the identical
/// <see cref="ServiceOrder.DispatchTo"/>/<see cref="ServiceOrder.RecordAction"/> calls. This is
/// the hybrid (part-agent, part-human) case falling out of configuration, not code.
/// </summary>
public sealed class DispatchServiceOrderExecutor(
    IServiceOrderRepository repository,
    ServiceOrderRules       rules,
    IAgentIdentityProvider  identity,
    string                  id = nameof(DispatchServiceOrderExecutor))
    : Executor<ServiceOrderCreated, ServiceOrder>(id)
{
    public override async ValueTask<ServiceOrder> HandleAsync(
        ServiceOrderCreated message,
        IWorkflowContext    context,
        CancellationToken   cancellationToken = default)
    {
        var principal = identity.Resolve(message.AgentId);
        var order     = message.Order;

        if (!rules.Evaluate(principal, OrderActionType.Dispatch, order).Allowed)
            return order;

        // AppendDispatchAsync/AppendActionAsync — not UpdateAsync, which explicitly does not
        // persist Dispatches/ActionLog (see IServiceOrderWriter.UpdateAsync). AppendActionAsync
        // applies the Draft -> Pending transition atomically with the audit entry.
        var dispatchedAt = DateTime.UtcNow;
        var dispatch = new OrderDispatch(
            message.DispatchTargetId,
            message.DispatchTargetType,
            message.AgentId,
            dispatchedAt)
        {
            ActorKind         = ActorKind.Agent,
            AgentInvocationId = message.AgentInvocationId
        };

        await repository.AppendDispatchAsync(order.Id, dispatch, cancellationToken);
        await repository.AppendActionAsync(
            order.Id,
            new OrderActionLog(
                OrderActionType.Dispatch,
                message.AgentId,
                dispatchedAt,
                ResultingStatus: ServiceOrderStatus.Pending)
            {
                ActorKind         = ActorKind.Agent,
                AgentInvocationId = message.AgentInvocationId
            },
            cancellationToken);

        // Whether AppendDispatchAsync/AppendActionAsync mutate `order` in place is an
        // implementation detail some repositories (in-memory) happen to do and others
        // (EF-backed) don't — re-read the authoritative persisted state instead of guessing.
        return (ServiceOrder)(await repository.GetByIdAsync(order.Id, cancellationToken))!;
    }
}
