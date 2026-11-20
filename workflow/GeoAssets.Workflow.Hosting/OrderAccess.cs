using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Hosting;

/// <summary>An order the caller was allowed on, or the problem to return instead (<see cref="OrderTargetEndpointSupport"/>).</summary>
public readonly record struct OrderAccess(IServiceOrder? Order, WorkflowPrincipal? Principal, IResult? Problem);
