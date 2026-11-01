namespace GeoAssets.Domain.Electrical.Continuity;

public sealed record ContinuityComplianceResult(IndicatorComplianceStatus Dic, IndicatorComplianceStatus Fic, IndicatorComplianceStatus Dmic)
{
    public bool AnyExceeded => Dic.Exceeded || Fic.Exceeded || Dmic.Exceeded;
}
