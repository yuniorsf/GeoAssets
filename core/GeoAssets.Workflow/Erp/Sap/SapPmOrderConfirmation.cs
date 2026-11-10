namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Outbound payload for SAP's <c>BAPI_ALM_ORDER_CONFIRMATION_CREATE</c> — built by
/// <see cref="SapPmOrderConfirmationDispatchService"/> on an order's transition to
/// <see cref="FieldWork.FieldWorkStatus.Inspected"/> and handed to
/// <see cref="ISapPmConfirmationDispatcher"/>.
/// </summary>
public sealed record SapPmOrderConfirmation(
    string OrderNumber,
    decimal LaborHours,
    IReadOnlyList<SapPmMaterialLine> Materials,
    string EvidenceDossierUrl,
    DateTimeOffset ConfirmedAtUtc);
