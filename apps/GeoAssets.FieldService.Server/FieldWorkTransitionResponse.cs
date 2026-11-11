using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>Successful outcome of a field-work transition attempt (XD01-295).</summary>
internal sealed record FieldWorkTransitionResponse(string ServiceOrderId, string Status, byte[] RowVersion)
{
    public static FieldWorkTransitionResponse FromDomain(ServiceOrder order) =>
        new(order.Id, order.Status, order.RowVersion);
}
