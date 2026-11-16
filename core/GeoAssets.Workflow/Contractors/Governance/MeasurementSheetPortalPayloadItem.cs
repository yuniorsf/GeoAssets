namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>One billed line item within <see cref="MeasurementSheetPortalPayload"/>.</summary>
public sealed record MeasurementSheetPortalPayloadItem(
    string ItemCode,
    decimal Quantity,
    decimal UnitUs,
    decimal TotalBrl);
