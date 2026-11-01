namespace GeoAssets.Domain.Electrical.Continuity;

public sealed record CompensationResult(decimal Dic, decimal Fic, decimal Dmic)
{
    public decimal Total => Dic + Fic + Dmic;
}
