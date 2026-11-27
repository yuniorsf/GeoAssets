using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="ILocalRasterTileProvider"/> declaring <paramref name="capabilities"/> (XD01-465), its tiles from <see cref="StubRasterTiles"/>.</summary>
internal sealed class FakeLocalRasterLayerProvider(
    ProviderRenderCapabilities capabilities = ProviderRenderCapabilities.RasterTiles,
    BoundingBox? bounds = null)
    : FakeLayerProvider(), IProviderCapabilities, ILocalRasterTileProvider
{
    public static readonly TileLayerSet Layers = new("pmtiles", ["pmtiles"]);

    public ProviderRenderCapabilities RenderCapabilities => capabilities;

    public StubRasterTiles Tiles { get; } = new();

    public IRasterTileSource RasterTiles => Tiles;

    public LocalRasterTileLayer RasterTileLayer { get; } = new(Layers, "image/webp", 2, 12, bounds);
}
