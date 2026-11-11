namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>One dump-truck hauling ticket to post against a Sienge cost center (XD01-254).</summary>
public sealed record HaulingTicketRecord(
    string CostCenterCode,
    string UsCode,
    decimal LoadVolumeCubicMeters,
    DateTime HaulDateUtc);
