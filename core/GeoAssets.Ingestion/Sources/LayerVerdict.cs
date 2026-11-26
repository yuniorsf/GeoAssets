namespace GeoAssets.Ingestion.Sources;

/// <summary>What the inventory concluded a layer file set can be used for.</summary>
public enum LayerVerdict
{
    /// <summary>Geometry and attribute table present and sane.</summary>
    Usable,

    /// <summary>Geometry present and sane, attribute table missing.</summary>
    GeometryOnly,

    /// <summary>No geometry, or geometry that failed the sanity checks — never imported.</summary>
    Unusable,
}
