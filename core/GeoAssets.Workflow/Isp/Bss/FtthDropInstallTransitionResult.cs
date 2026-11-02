using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// Result of <see cref="FtthDropInstallActivationDispatchService.TransitionAsync"/>. The status
/// transition and its persistence always succeeded by the time this is returned — a dispatch
/// failure is reported as data here, not an exception, since by that point there is nothing left
/// to roll back.
/// </summary>
public sealed record FtthDropInstallTransitionResult(
    IServiceOrder Order,
    bool ActivationWebhookDispatched,
    string? ActivationWebhookFailureReason);
