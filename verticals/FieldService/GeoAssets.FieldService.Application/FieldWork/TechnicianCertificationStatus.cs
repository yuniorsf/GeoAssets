namespace GeoAssets.FieldService.FieldWork;

/// <summary>One certification's expiry, as pulled from Totvs Protheus.</summary>
public sealed record TechnicianCertificationStatus(TechnicianCertificationType Type, DateTime ExpiresOnUtc);
