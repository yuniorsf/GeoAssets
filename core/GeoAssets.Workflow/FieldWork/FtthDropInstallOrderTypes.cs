using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Canonical <see cref="OrderType"/> registration for <see cref="FtthDropInstallWorkflowGraph"/>
/// (XD01-273), previously built but never registered anywhere (XD01-280 is the first ticket to
/// need a host actually create/transition this order type). Same <c>RegisterAll(OrderTypeRegistry)</c>
/// shape as <see cref="FieldWorkOrderTypes"/>, for use with
/// <c>services.AddOrderTypeRegistry(registry => { FieldWorkOrderTypes.RegisterAll(registry); FtthDropInstallOrderTypes.RegisterAll(registry); })</c>.
/// </summary>
public static class FtthDropInstallOrderTypes
{
    public const string FtthDropInstallId = "ftth-drop-install";

    public static OrderType Create() =>
        FtthDropInstallWorkflowGraph.BuildOrderType(FtthDropInstallId, "FTTH Drop Install");

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(Create());
    }
}
