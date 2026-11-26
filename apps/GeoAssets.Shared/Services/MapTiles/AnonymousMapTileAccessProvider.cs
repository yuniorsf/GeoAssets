using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.MapTiles;

/// <summary>
/// The fallback <see cref="IMapTileAccessProvider"/> of a host with no token source (XD01-96): every layer's tiles are
/// requested without a token, so this app's own server refuses them and the layer stays empty.
/// </summary>
public sealed class AnonymousMapTileAccessProvider : IMapTileAccessProvider
{
    public Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tileUrl);
        return Task.FromResult(MapTileAccess.Anonymous);
    }
}
