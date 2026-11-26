using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.MapTiles;

/// <summary>
/// What <c>MapContainer</c> answers when <c>geoassets.js</c> asks how to load a WMS layer's tiles (XD01-96). A URL that
/// isn't an absolute http(s) URL, or a provider that fails, gets <see cref="MapTileAccess.Unavailable"/>: the layer shows nothing rather
/// than being requested without the token it may need.
/// </summary>
public static partial class MapTileAccessLookup
{
    public static async Task<MapTileAccess> GetAsync(IMapTileAccessProvider provider, string? tileUrl, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);

        if (!Uri.TryCreate(tileUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            LogNotAbsolute(logger);
            return MapTileAccess.Unavailable;
        }

        try
        {
            return await provider.GetAccessAsync(uri).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogProviderFailed(logger, uri.GetLeftPart(UriPartial.Path), ex);
            return MapTileAccess.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map tile access refused: the layer's URL is not an absolute http(s) URL")]
    private static partial void LogNotAbsolute(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map tile access unavailable for {TileUrl}: the token source failed")]
    private static partial void LogProviderFailed(ILogger logger, string tileUrl, Exception ex);
}
