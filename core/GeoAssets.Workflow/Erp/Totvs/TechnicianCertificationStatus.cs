namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>One certification's expiry, as pulled from Totvs Protheus.</summary>
public sealed record TechnicianCertificationStatus(TechnicianCertificationType Type, DateTime ExpiresOnUtc);
