namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>A file is not a PMTiles v3 archive this reader can serve, or is corrupt (XD01-437).</summary>
public sealed class PMTilesFormatException : IOException
{
    public PMTilesFormatException()
    {
    }

    public PMTilesFormatException(string message) : base(message)
    {
    }

    public PMTilesFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
