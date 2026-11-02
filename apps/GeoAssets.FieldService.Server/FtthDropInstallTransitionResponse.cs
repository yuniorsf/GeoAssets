using GeoAssets.Workflow.Isp.Bss;

namespace GeoAssets.FieldService.Server;

internal sealed record FtthDropInstallTransitionResponse(string OrderId, string Status, bool ActivationWebhookDispatched, string? ActivationWebhookFailureReason)
{
    public static FtthDropInstallTransitionResponse FromResult(FtthDropInstallTransitionResult result) =>
        new(result.Order.Id, result.Order.Status, result.ActivationWebhookDispatched, result.ActivationWebhookFailureReason);
}
