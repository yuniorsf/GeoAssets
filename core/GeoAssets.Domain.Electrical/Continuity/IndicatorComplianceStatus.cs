namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>Whether one apurado indicator value exceeded its limit, and by how much (as a fraction of the limit — 0 means exactly at the limit, 1 means double).</summary>
public sealed record IndicatorComplianceStatus(bool Exceeded, double ExcessRatio);
