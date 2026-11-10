namespace GeoAssets.FieldService.Server;

/// <summary>Raw envelope for the SAP PM inbound webhook endpoint (XD01-231) — <see cref="PayloadJson"/> is handed to <see cref="GeoAssets.Workflow.Erp.Sap.ISapPmOrderAdapter"/> unparsed.</summary>
internal sealed record SapPmWebhookIngestRequest(string PayloadJson);
