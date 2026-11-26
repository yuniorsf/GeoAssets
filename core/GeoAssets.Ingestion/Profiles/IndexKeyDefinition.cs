namespace GeoAssets.Ingestion.Profiles;

/// <summary>
/// A key under which the pipeline indexes a layer's elements (pass 1) so references can resolve by key:
/// records whose <see cref="Field"/> is present and non-zero are indexed as (<see cref="Namespace"/>, value).
/// </summary>
public sealed record IndexKeyDefinition(string Namespace, string Field);
