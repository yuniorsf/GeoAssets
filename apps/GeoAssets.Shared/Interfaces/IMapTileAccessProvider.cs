namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Whether, and with which token, the map requests a WMS layer's tiles (XD01-96). Each host answers with the token source
/// it already uses for this app's own server (Web: MSAL's <c>IAccessTokenProvider</c>; MAUI: MSAL.NET), and only for URLs
/// under that server's configured base URL; a token is never handed out for any other host. A host that registers none
/// gets <see cref="Services.MapTiles.AnonymousMapTileAccessProvider"/>.
/// </summary>
public interface IMapTileAccessProvider
{
    /// <summary>The access for a layer whose tiles are requested from <paramref name="tileUrl"/> (absolute).</summary>
    Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default);
}
