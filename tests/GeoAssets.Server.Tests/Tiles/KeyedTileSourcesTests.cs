using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-459 — <see cref="KeyedTileSources"/>: the keyed <see cref="IVectorTileSource"/>/<see cref="IRasterTileSource"/>
/// registrations under a tile-key-segment string, read from the services once they are complete, resolved through the
/// Core contracts only when they describe themselves.
/// </summary>
public sealed class KeyedTileSourcesTests
{
    private static readonly TileSourceDescription Described = new("v1", ["a"], 0, 10);

    [Fact]
    public void From_ListsTheStringKeyedTileSources_OfEachFormat_InOrder_SkippingEverythingElse()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVectorTileSource>(new DescribedTestSource("unkeyed", Described));
        services.AddKeyedSingleton<IVectorTileSource>("basemap", new DescribedTestSource("basemap", Described));
        services.AddKeyedSingleton<IVectorTileSource>("roads", new DescribedTestSource("roads", Described));
        services.AddKeyedSingleton<IVectorTileSource>("roads", new DescribedTestSource("roads", Described));
        services.AddKeyedSingleton<IVectorTileSource>("not/a segment", new DescribedTestSource("x", Described));
        services.AddKeyedSingleton<IVectorTileSource>(42, new DescribedTestSource("x", Described));
        services.AddKeyedSingleton<IRasterTileSource>("ortho", new DescribedTestSource("ortho", Described));
        services.AddKeyedSingleton<ITileCache>("basemap", new MemoryTileCache());

        var sources = KeyedTileSources.From(services);

        sources.VectorIds.Should().Equal("basemap", "roads");
        sources.RasterIds.Should().Equal("ortho");
    }

    [Fact]
    public void AddTileServing_ReadsThemOnceTheServicesAreComplete_SoAPluginRegisteredAfterwardsIsListed()
    {
        var services = new ServiceCollection();
        services.AddTileServing(new ConfigurationBuilder().Build());
        services.AddKeyedSingleton<IRasterTileSource>("ortho", new DescribedTestSource("ortho", Described));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<KeyedTileSources>().RasterIds.Should().Equal("ortho");
    }

    [Fact]
    public void Resolve_IsTheKeyedServiceOfThatFormat_AndNothingForAnotherIdFormatOrAnUndescribedSource()
    {
        var services = new ServiceCollection();
        var basemap = new DescribedTestSource("basemap", Described);
        services.AddKeyedSingleton<IVectorTileSource>("basemap", basemap);
        services.AddKeyedSingleton<IRasterTileSource>("plain", new PlainRasterSource());
        var sources = KeyedTileSources.From(services);
        using var provider = services.BuildServiceProvider();

        var resolved = sources.Resolve(provider, "basemap", TileFormat.Mvt);

        resolved.Should().NotBeNull();
        resolved!.Source.Should().BeSameAs(basemap);
        resolved.Described.Should().BeSameAs(basemap);
        sources.Resolve(provider, "basemap", TileFormat.Png).Should().BeNull();
        sources.Resolve(provider, "other", TileFormat.Mvt).Should().BeNull();
        sources.Resolve(provider, "plain", TileFormat.Png).Should().BeNull("a source that can't describe itself is not served");
        sources.ResolveAll(provider).Select(s => s.Id).Should().Equal("basemap");
    }

    private sealed class PlainRasterSource : IRasterTileSource
    {
        public Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct) =>
            Task.FromResult(TilePayload.Empty(TileFormats.PngContentType));
    }
}
