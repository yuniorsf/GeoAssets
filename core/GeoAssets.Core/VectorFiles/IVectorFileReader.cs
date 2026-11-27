namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// Reads cloud-native vector files (FlatGeobuf, GeoParquet) page by page into <see cref="Models.GeoFeature"/>s
/// (ADR-002 D5, XD01-437). A shared contract in GeoAssets.Core, so the GDAL plugin's implementation and the host meet in
/// the default load context.
/// </summary>
public interface IVectorFileReader
{
    /// <summary>Whether the runtime this process loaded can read <paramref name="format"/> (its driver is there).</summary>
    bool IsAvailable(VectorFileFormat format);

    /// <summary>The page <paramref name="request"/> names.</summary>
    /// <exception cref="VectorFileFormatUnavailableException">The runtime can't read <see cref="VectorFileReadRequest.Format"/>.</exception>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidDataException">The file isn't a readable file of that format, or has no such layer.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    VectorFilePage ReadPage(VectorFileReadRequest request, CancellationToken ct);
}
