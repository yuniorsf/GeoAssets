namespace GeoAssets.Domain.Sanitation.Icerp;

/// <summary>
/// The excavated trench's footprint, as actually dug — before any reinstatement margin is added.
///
/// <paramref name="DepthMeters"/> defaults to 0 — most existing callers (e.g.
/// <see cref="PavementReinstatementCalculator"/>) only ever needed the footprint, not the
/// excavation volume, so this stays additive rather than forcing every call site to supply a
/// depth it has no use for.
/// </summary>
public sealed record TrenchDimensions(double LengthMeters, double WidthMeters, double DepthMeters = 0)
{
    public double AreaSquareMeters => LengthMeters * WidthMeters;

    /// <summary>The raw excavated volume — <see cref="AreaSquareMeters"/> × <see cref="DepthMeters"/>, not to be confused with <see cref="CbuqReinstatementCalculator"/>'s reinstatement-layer volume (a different depth: the new pavement's thickness, not how deep the trench was dug).</summary>
    public double VolumeCubicMeters => AreaSquareMeters * DepthMeters;
}
