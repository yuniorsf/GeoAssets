namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>One material line reported on an outbound SAP PM order confirmation.</summary>
public sealed record SapPmMaterialLine(string ItemCode, decimal Quantity);
