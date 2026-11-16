using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>Result of <see cref="SapPmOrderConfirmationDispatchService.TransitionToInspectedAsync"/>.</summary>
public sealed record SapPmConfirmationDispatchResult(
    IServiceOrder Order,
    bool ConfirmationDispatched,
    string? ConfirmationFailureReason);
