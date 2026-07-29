using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>
/// Carries a freshly created order plus the actor/dispatch context forward from
/// <see cref="CreateServiceOrderExecutor"/> to <see cref="DispatchServiceOrderExecutor"/>.
/// </summary>
public sealed record ServiceOrderCreated(
    ServiceOrder       Order,
    string             AgentId,
    string             DispatchTargetId,
    DispatchTargetType DispatchTargetType,
    string?            AgentInvocationId);
