namespace GeoAssets.Provider.Gdal;

/// <summary>
/// The native GDAL runtime can't be loaded in this process (XD01-437): the platform has no MaxRev runtime (Windows,
/// iOS, Android, Mac Catalyst) or the install is broken. See <see cref="GdalNativeInitializer"/>.
/// </summary>
public sealed class GdalNativeUnavailableException : PlatformNotSupportedException
{
    public GdalNativeUnavailableException()
    {
    }

    public GdalNativeUnavailableException(string message) : base(message)
    {
    }

    public GdalNativeUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
