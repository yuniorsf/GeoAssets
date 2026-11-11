namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>One civil-crew equipment-hour claim to post against a Sienge cost center (XD01-254).</summary>
public sealed record MachineHourRecord(
    string CostCenterCode,
    string UsCode,
    decimal Hours,
    DateTime WorkDateUtc);
