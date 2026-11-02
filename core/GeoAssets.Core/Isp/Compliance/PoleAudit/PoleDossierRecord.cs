namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>Links a generated concessionaire compliance dossier (XD01-284) to the pole it was generated for.</summary>
public sealed record PoleDossierRecord(
    string PoleFeatureId,
    string DistributorName,
    DateTimeOffset GeneratedAtUtc,
    ConcessionaireDossierResult Dossier);
