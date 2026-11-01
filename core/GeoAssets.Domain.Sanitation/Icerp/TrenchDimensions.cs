namespace GeoAssets.Domain.Sanitation.Icerp;

/// <summary>The excavated trench's footprint, as actually dug — before any reinstatement margin is added.</summary>
public sealed record TrenchDimensions(double LengthMeters, double WidthMeters)
{
    public double AreaSquareMeters => LengthMeters * WidthMeters;
}
