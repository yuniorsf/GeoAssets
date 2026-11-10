using GeoAssets.Workflow.Erp.Sap;

namespace GeoAssets.FieldService.Server;

/// <summary>Response for the SAP PM order-confirmation endpoint (XD01-231).</summary>
internal sealed record SapPmConfirmationResponse(bool ConfirmationDispatched, string? ConfirmationFailureReason)
{
    public static SapPmConfirmationResponse FromResult(SapPmConfirmationDispatchResult result) =>
        new(result.ConfirmationDispatched, result.ConfirmationFailureReason);
}
