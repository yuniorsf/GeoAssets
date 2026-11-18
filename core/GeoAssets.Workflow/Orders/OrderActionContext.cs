using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Orders;

/// <summary>What an <see cref="IOrderActionHandler"/> acts on: the order, its type, the requested entry and the caller.</summary>
public sealed record OrderActionContext(IServiceOrder Order, OrderType OrderType, OrderActionLog Entry, WorkflowPrincipal Principal);
