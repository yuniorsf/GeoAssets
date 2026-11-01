using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Canonical <see cref="OrderType"/> registration for <see cref="FieldWorkWorkflowGraph"/>
/// (XD01-228) — same <c>RegisterAll(OrderTypeRegistry)</c> shape as
/// <c>GeoAssets.IpBilling.Workflow.IpBillingOrderTypes</c>, for use with
/// <c>services.AddOrderTypeRegistry(FieldWorkOrderTypes.RegisterAll)</c>. "field-work" matches
/// the id already used throughout this graph's own tests (<see cref="FieldWorkWorkflowGraph"/>'s
/// callers in <c>tests/GeoAssets.Workflow.Tests/FieldWork</c>).
/// </summary>
public static class FieldWorkOrderTypes
{
    public const string FieldWorkId = "field-work";

    public static OrderType Create() =>
        FieldWorkWorkflowGraph.BuildOrderType(FieldWorkId, "Field Work Order");

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(Create());
    }
}
