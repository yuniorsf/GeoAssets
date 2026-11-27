using GeoAssets.Core.Models;

namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// Writes <see cref="GeoFeature"/>s into a PMTiles v3 archive of Mapbox Vector Tiles as they stream in (XD01-472) — the
/// tiled counterpart of <see cref="IVectorFileWriter"/>, readable by the GDAL-free PMTiles reader
/// (<c>PMTilesVectorTileSource</c>) and importable as an offline map pack. A shared contract in GeoAssets.Core, so the
/// GDAL plugin's implementation and the host meet in the default load context.
/// <para>
/// <b>Tiles.</b> Every zoom of <see cref="VectorTileArchiveWriteRequest.MinZoom"/> … <see cref="VectorTileArchiveWriteRequest.MaxZoom"/>
/// is tiled (Web Mercator, XYZ). Each feature goes to the MVT layer of its
/// <see cref="GeoFeatureProperties.AssetTypeId"/> (<see cref="VectorTileArchiveWriteRequest.LayerNames"/>, else named
/// after the asset type id) and is drawn in every tile it touches at every zoom.
/// </para>
/// <para>
/// <b>Identity.</b> A feature's MVT feature id is its 1-based position in the input — unique across the archive and the
/// same in every tile and zoom that holds a piece of it, so a reader can join a feature's pieces by it (offline packs
/// do, XD01-463). A <see cref="GeoFeature.Id"/> is text and an MVT id an unsigned integer, so the feature's own id
/// travels in the <c>id</c> tag, its <see cref="GeoFeatureProperties.Name"/> in <c>name</c> and each of its
/// <see cref="GeoFeatureProperties.CustomAttributes"/> as a text tag of the same name (a key spelled <c>id</c> or
/// <c>name</c>, in any case, is left to those two).
/// </para>
/// <para>
/// A feature without geometry can't be tiled: it is skipped and counted in
/// <see cref="VectorTileArchiveWriteResult.FeaturesSkipped"/> (an input with none to tile fails the write). The input is enumerated once and never held in memory as
/// a whole.
/// </para>
/// </summary>
public interface IVectorTileArchiveWriter
{
    /// <summary>Whether the runtime this process loaded can write PMTiles archives (its driver is there).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Writes <paramref name="features"/> to the archive <paramref name="path"/>, replacing it. The archive appears only
    /// once complete: a failed or cancelled write leaves no file behind (and an existing one untouched).
    /// </summary>
    /// <exception cref="VectorTileArchiveUnavailableException">The runtime can't write PMTiles archives.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/>'s folder doesn't exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The features can't be written: e.g. two attributes of one layer whose names differ only by case, or no feature
    /// has a geometry (an archive holds at least one).
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<VectorTileArchiveWriteResult> WriteAsync(
        string path, VectorTileArchiveWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct);

    /// <summary>Writes <paramref name="features"/> as one archive onto <paramref name="destination"/>, which is left open.</summary>
    /// <exception cref="VectorTileArchiveUnavailableException">The runtime can't write PMTiles archives.</exception>
    /// <exception cref="InvalidDataException">The features can't be written (as for the path overload).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<VectorTileArchiveWriteResult> WriteAsync(
        Stream destination, VectorTileArchiveWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct);
}
