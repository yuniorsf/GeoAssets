using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="IRasterTileSource"/> answering <see cref="Images"/> by tile, empty elsewhere, and recording what it is asked.</summary>
internal sealed class StubRasterTiles : IRasterTileSource
{
    public Dictionary<TileAddress, byte[]> Images { get; } = [];

    public List<TileRequest> Requests { get; } = [];

    public Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Images.TryGetValue(request.Address, out var image)
            ? new TilePayload(image, "image/webp")
            : TilePayload.Empty("image/webp"));
    }
}
