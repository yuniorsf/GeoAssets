namespace GeoAssets.Core.VectorFiles;

/// <summary>A cloud-native vector file format an <see cref="IVectorFileReader"/> reads and an <see cref="IVectorFileWriter"/> writes (ADR-002 D5, XD01-437, XD01-466).</summary>
public enum VectorFileFormat
{
    /// <summary>No format — never valid in a <see cref="VectorFileReadRequest"/> or a <see cref="VectorFileWriteRequest"/>.</summary>
    None = 0,

    /// <summary>FlatGeobuf (<c>.fgb</c>).</summary>
    FlatGeobuf = 1,

    /// <summary>GeoParquet (<c>.parquet</c>).</summary>
    GeoParquet = 2,
}
