using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

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
public sealed partial class DispatchServiceOrderExecutor(
    IServiceOrderRepository                repository,
    ServiceOrderRules                      rules,
    IAgentIdentityProvider                 identity,
    TimeProvider                            timeProvider,
    GeoAssetsActivitySource                 tracer,
    ILogger<DispatchServiceOrderExecutor>   logger,
    string                                   id = nameof(DispatchServiceOrderExecutor))
    : Executor<ServiceOrderCreated, ServiceOrder>(id)
{
    public override async ValueTask<ServiceOrder> HandleAsync(
        ServiceOrderCreated message,
        IWorkflowContext    context,
        CancellationToken   cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var span = tracer.StartAgentActivity("Dispatch", message.Order.Id, message.AgentId, message.AgentInvocationId);

        var principal = identity.Resolve(message.AgentId);
        var order     = message.Order;

        var decision = rules.Evaluate(principal, OrderActionType.Dispatch, order);
        span?.AddTag("decision.allowed", decision.Allowed);

        if (!decision.Allowed)
        {
            LogDenied(logger, message.AgentId, order.Id, decision.Reason);
            return order;
        }

        try
        {
            return await DispatchAndActivateAsync(message, order, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(span, ex);
            LogDispatchFailed(logger, message.AgentId, order.Id, ex);
            throw;
        }
    }

    /// <summary>
    /// AppendDispatchAsync/AppendActionAsync — not UpdateAsync, which explicitly does not persist
    /// Dispatches/ActionLog (see IServiceOrderWriter.UpdateAsync). AppendActionAsync applies the
    /// Draft -> Pending transition atomically with the audit entry.
    /// </summary>
    private async Task<ServiceOrder> DispatchAndActivateAsync(
        ServiceOrderCreated message, ServiceOrder order, CancellationToken cancellationToken)
    {
        var dispatchedAt = timeProvider.GetUtcNow().UtcDateTime;
        var dispatch = new OrderDispatch(
            message.DispatchTargetId,
            message.DispatchTargetType,
            message.AgentId,
            dispatchedAt)
        {
            ActorKind         = ActorKind.Agent,
            AgentInvocationId = message.AgentInvocationId
        };

        await repository.AppendDispatchAsync(order.Id, dispatch, cancellationToken).ConfigureAwait(false);
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
            cancellationToken).ConfigureAwait(false);

        // Whether AppendDispatchAsync/AppendActionAsync mutate `order` in place is an
        // implementation detail some repositories (in-memory) happen to do and others
        // (EF-backed) don't — re-read the authoritative persisted state instead of guessing.
        var updated = (ServiceOrder)(await repository.GetByIdAsync(order.Id, cancellationToken).ConfigureAwait(false))!;

        LogDispatched(logger, message.AgentId, updated.Id, updated.Status);

        return updated;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} denied Dispatch on order {OrderId}: {Reason}")]
    private static partial void LogDenied(ILogger logger, string agentId, string orderId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} dispatched order {OrderId}; transitioned to {NewStatus}")]
    private static partial void LogDispatched(ILogger logger, string agentId, string orderId, string newStatus);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent {AgentId} failed to dispatch order {OrderId}")]
    private static partial void LogDispatchFailed(ILogger logger, string agentId, string orderId, Exception ex);
}
