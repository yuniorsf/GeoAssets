namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// The runtime this process loaded can't write vector tile archives (XD01-472) — e.g. a GDAL build without the PMTiles
/// driver, or a platform without the native GDAL runtime at all. Thrown when the writer is used, never at startup.
/// </summary>
public sealed class VectorTileArchiveUnavailableException : NotSupportedException
{
    public VectorTileArchiveUnavailableException()
    {
    }

    public VectorTileArchiveUnavailableException(string message) : base(message)
    {
    }

    public VectorTileArchiveUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
