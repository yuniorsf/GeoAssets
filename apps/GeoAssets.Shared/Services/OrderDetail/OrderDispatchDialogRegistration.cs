using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.OrderDetail;

/// <summary>A dispatch dialog of the order detail, used for the order types <paramref name="AppliesTo"/> accepts.</summary>
/// <param name="Component">The dialog's component type.</param>
/// <param name="AppliesTo">Whether the dialog dispatches orders of a type.</param>
public sealed record OrderDispatchDialogRegistration(Type Component, Func<OrderType, bool> AppliesTo);
