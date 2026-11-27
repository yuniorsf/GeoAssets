namespace GeoAssets.Core.VectorFiles;

/// <summary>A cloud-native vector file format an <see cref="IVectorFileReader"/> reads (ADR-002 D5, XD01-437).</summary>
public enum VectorFileFormat
{
    /// <summary>No format — never valid in a <see cref="VectorFileReadRequest"/>.</summary>
    None = 0,

    /// <summary>FlatGeobuf (<c>.fgb</c>).</summary>
    FlatGeobuf = 1,

    /// <summary>GeoParquet (<c>.parquet</c>).</summary>
    GeoParquet = 2,
}
