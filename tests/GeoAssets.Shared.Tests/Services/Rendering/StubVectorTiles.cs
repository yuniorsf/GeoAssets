using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="IVectorTileSource"/> answering the tiles it was given, as given.</summary>
internal sealed class StubVectorTiles : IVectorTileSource
{
    public Dictionary<TileAddress, TilePayload> Tiles { get; } = [];

    public List<TileRequest> Requests { get; } = [];

    public Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Tiles.TryGetValue(request.Address, out var tile)
            ? tile
            : TilePayload.Empty(TileFormats.MapboxVectorTileContentType));
    }
}
