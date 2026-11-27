using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>XD01-434 — <see cref="TileServingOptions"/> from <c>GeoAssets:Tiles</c>: defaults, overrides, fail-fast on invalid values.</summary>
public sealed class TileServingOptionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"{TileServingOptions.SectionKey}:{s.Key}", s.Value)))
            .Build();

    [Fact]
    public void WithoutASection_EveryValueIsTheDefault()
    {
        var options = TileServingOptions.From(Config());

        options.MaxCachedZoom.Should().Be(18);
        options.MaxEvictedTilesPerWrite.Should().Be(65_536);
        options.CacheMaxBytes.Should().Be(256L * 1024 * 1024);
        options.Lod.Default.Should().Be(LodThresholds.VectorOnly);
        options.Lod.Layers.Should().BeEmpty();
    }

    [Fact]
    public void EveryValue_IsRead_IncludingPerLayerThresholds()
    {
        var options = TileServingOptions.From(Config(
            ("MaxCachedZoom", "16"),
            ("MaxEvictedTilesPerWrite", "1000"),
            ("CacheMaxMegabytes", "64"),
            ("Lod:RasterFromZoom", "6"),
            ("Lod:VectorFromZoom", "12"),
            ("Lod:Layers:poles:RasterFromZoom", "10"),
            ("Lod:Layers:poles:VectorFromZoom", "15"),
            ("Lod:Layers:poles:MinZoom", "8")));

        options.MaxCachedZoom.Should().Be(16);
        options.MaxEvictedTilesPerWrite.Should().Be(1000);
        options.CacheMaxBytes.Should().Be(64L * 1024 * 1024);
        options.Lod.Default.Should().Be(new LodThresholds(6, 12));
        options.Lod.GetThresholds("poles").Should().Be(new LodThresholds(10, 15, MinZoom: 8));
        options.Lod.Decide("other", 7).Should().Be(TileRenderTier.Raster);
    }

    [Theory]
    [InlineData("MaxCachedZoom", "25")]
    [InlineData("MaxCachedZoom", "-1")]
    [InlineData("MaxCachedZoom", "eighteen")]
    [InlineData("MaxEvictedTilesPerWrite", "0")]
    [InlineData("CacheMaxMegabytes", "0")]
    [InlineData("CacheMaxMegabytes", "lots")]
    public void AnInvalidValue_FailsTheStartup(string key, string value)
    {
        var act = () => TileServingOptions.From(Config((key, value)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void InvalidLodThresholds_FailTheStartup()
    {
        var act = () => TileServingOptions.From(Config(("Lod:RasterFromZoom", "14"), ("Lod:VectorFromZoom", "10")));

        act.Should().Throw<InvalidLodPolicyException>();
    }
}
