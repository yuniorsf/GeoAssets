using GeoAssets.Workflow.Orders;

namespace GeoAssets.Integrations.Erp.Sap;

/// <summary>Result of <see cref="SapPmOrderConfirmationDispatchService.TransitionToInspectedAsync"/>.</summary>
public sealed record SapPmConfirmationDispatchResult(
    IServiceOrder Order,
    bool ConfirmationDispatched,
    string? ConfirmationFailureReason);
