using GeoAssets.Core.Models;

namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// Writes <see cref="GeoFeature"/>s into a cloud-native vector file (FlatGeobuf, GeoParquet) as they stream in — the
/// counterpart of <see cref="IVectorFileReader"/> (XD01-466). A shared contract in GeoAssets.Core, so the GDAL plugin's
/// implementation and the host meet in the default load context.
/// <para>
/// Each feature becomes one row: its geometry in EPSG:4326 (longitude, latitude), its <see cref="GeoFeature.Id"/> in an
/// <c>id</c> column, its <see cref="GeoFeatureProperties.Name"/> in a <c>name</c> column and each of its
/// <see cref="GeoFeatureProperties.CustomAttributes"/> in a text column of the same name (a key spelled <c>id</c> or
/// <c>name</c>, in any case, is left to those two columns) — so <see cref="IVectorFileReader"/> reads back the same
/// geometry, id, name and attributes, plus the <c>id</c> and <c>name</c> columns themselves among the attributes. A
/// FlatGeobuf file is written with its spatial index, which needs a geometry on every feature; GeoParquet also holds
/// features without one. The input is enumerated once and never held in memory as a whole.
/// </para>
/// </summary>
public interface IVectorFileWriter
{
    /// <summary>Whether the runtime this process loaded can write <paramref name="format"/> (its driver is there).</summary>
    bool IsAvailable(VectorFileFormat format);

    /// <summary>
    /// Writes <paramref name="features"/> to the file <paramref name="path"/>, replacing it. The file appears only once
    /// complete: a failed or cancelled write leaves no file behind (and an existing one untouched).
    /// </summary>
    /// <exception cref="VectorFileFormatUnavailableException">The runtime can't write <see cref="VectorFileWriteRequest.Format"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/>'s folder doesn't exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The features can't be written: e.g. two attributes whose names differ only by case, or a FlatGeobuf feature without
    /// geometry.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<VectorFileWriteResult> WriteAsync(
        string path, VectorFileWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct);

    /// <summary>Writes <paramref name="features"/> as one file onto <paramref name="destination"/>, which is left open.</summary>
    /// <exception cref="VectorFileFormatUnavailableException">The runtime can't write <see cref="VectorFileWriteRequest.Format"/>.</exception>
    /// <exception cref="InvalidDataException">The features can't be written (as for the path overload).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<VectorFileWriteResult> WriteAsync(
        Stream destination, VectorFileWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct);
}
