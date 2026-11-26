namespace GeoAssets.Ingestion.Model;

/// <summary>
/// Identity of a source element: the profile, a key namespace (e.g. <c>PG</c>, <c>INST</c>, <c>TRECHO_MT</c>) and the
/// natural key within it. Elements without a natural key use a coordinate key (<see cref="ForCoordinates"/>).
/// </summary>
public readonly record struct SourceKey(string Profile, string Namespace, string NaturalKey)
{
    /// <summary>Coordinate key for elements without a natural key: native coordinates rounded to centimetres.</summary>
    public static SourceKey ForCoordinates(string profile, string @namespace, double x, double y) =>
        new(profile, @namespace, FormattableString.Invariant($"xy:{Math.Round(x, 2):0.00}:{Math.Round(y, 2):0.00}"));

    public override string ToString() => $"{Profile}|{Namespace}|{NaturalKey}";
}
