namespace GeoAssets.Core.VectorFiles;

/// <summary>What an <see cref="IVectorTileArchiveWriter"/> wrote (XD01-472).</summary>
/// <param name="FeaturesWritten">How many features the archive's tiles hold (each with its MVT id).</param>
/// <param name="FeaturesSkipped">How many input features had no geometry, so no tile holds them.</param>
/// <param name="Layers">The MVT layers written, in first-seen order.</param>
/// <param name="MinZoom">The archive's lowest zoom.</param>
/// <param name="MaxZoom">The archive's highest zoom.</param>
public sealed record VectorTileArchiveWriteResult(
    long FeaturesWritten, long FeaturesSkipped, IReadOnlyList<string> Layers, int MinZoom, int MaxZoom);
