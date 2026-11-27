using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="ILocalVectorTileProvider"/> declaring <paramref name="capabilities"/> (XD01-460), its tiles from <see cref="StubVectorTiles"/>.</summary>
internal sealed class FakeLocalVectorLayerProvider(
    ProviderRenderCapabilities capabilities = ProviderRenderCapabilities.VectorFeatures,
    BoundingBox? bounds = null)
    : FakeLayerProvider(), IProviderCapabilities, ILocalVectorTileProvider
{
    public static readonly TileLayerSet Layers = new("pmtiles", ["pmtiles"]);

    public ProviderRenderCapabilities RenderCapabilities => capabilities;

    public StubVectorTiles Tiles { get; } = new();

    public IVectorTileSource VectorTiles => Tiles;

    public LocalVectorTileLayer VectorTileLayer { get; } = new(Layers, ["poles"], 2, 12, bounds);
}
