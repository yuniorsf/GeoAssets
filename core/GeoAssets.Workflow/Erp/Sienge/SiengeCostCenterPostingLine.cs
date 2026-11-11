namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>One mapped line pushed to Sienge's cost-center posting API (XD01-254).</summary>
public sealed record SiengeCostCenterPostingLine(
    string CostCenterCode,
    string UsCode,
    decimal Quantity,
    DateTime OccurredUtc);
