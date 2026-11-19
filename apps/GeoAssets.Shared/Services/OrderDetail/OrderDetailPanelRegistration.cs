namespace GeoAssets.Shared.Services.OrderDetail;

/// <summary>A panel of the order detail, shown for order types that declare <paramref name="Capability"/>.</summary>
/// <param name="Component">The panel's component type.</param>
/// <param name="Capability">The order-type capability the panel belongs to (<see cref="Workflow.Orders.OrderType.HasCapability"/>).</param>
public sealed record OrderDetailPanelRegistration(Type Component, string Capability);
