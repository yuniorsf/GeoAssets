using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>Input message for <see cref="CreateServiceOrderExecutor"/>.</summary>
public sealed record CreateServiceOrderRequest(
    string             AgentId,
    string             OrderTypeId,
    string             Title,
    string             DispatchTargetId,
    DispatchTargetType DispatchTargetType,
    string?            AgentInvocationId = null);
