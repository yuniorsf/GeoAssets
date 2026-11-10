using GeoAssets.Workflow.Erp.Sap;

namespace GeoAssets.FieldService.Server;

/// <summary>Request body for the SAP PM order-confirmation endpoint (XD01-231).</summary>
internal sealed record SapPmConfirmationRequest(decimal LaborHours, IReadOnlyList<SapPmMaterialLine> Materials, string EvidenceDossierUrl);
