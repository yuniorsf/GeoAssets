namespace GeoAssets.Ingestion.Model;

/// <summary>How complete the source data of an element was (stamped as <c>import.completeness</c>).</summary>
public enum Completeness
{
    /// <summary>Geometry and attributes; references resolved by key.</summary>
    Full,

    /// <summary>Geometry only (the attribute table was missing); no references.</summary>
    GeometryOnly,

    /// <summary>Geometry only, with references recovered spatially.</summary>
    Recovered,
}
