using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Tests.Providers;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

public class AssetProviderRenderQueryTests
{
    private static readonly BoundingBox Whole = new(-10, -10, 10, 10);

    private readonly TestAssetProvider _catalog = new();
    private readonly AssetType _poles = new() { Name = "Poles", Color = "#aa0000" };
    private readonly AssetType _boxes = new() { Name = "Boxes", Color = "#00aa00" };

    public AssetProviderRenderQueryTests()
    {
        _catalog.AddAssetType(_poles);
        _catalog.AddAssetType(_boxes);
    }

    private AssetProviderRenderQuery NewSut() => new(_catalog);

    private void Seed(string id, AssetType type, GeoGeometry? geometry, string description = "")
    {
        var feature = new GeoFeature
        {
            Id = id,
            Geometry = geometry,
            Properties = { Name = $"name-{id}", AssetTypeId = type.Id.ToString(), Description = description },
        };
        _catalog.Add(feature);
    }

    [Fact]
    public async Task GetRenderableAsync_ReturnsFeaturesInTheBox_StyledByTheCatalog()
    {
        Seed("in", _poles, new GeoPoint(1, 1));
        Seed("out", _poles, new GeoPoint(50, 50));

        var result = await NewSut().GetRenderableAsync(Whole, new RenderQueryOptions(), CancellationToken.None);

        var feature = result.Should().ContainSingle().Subject;
        feature.Id.Should().Be("in");
        feature.Name.Should().Be("name-in");
        feature.AssetTypeId.Should().Be(_poles.Id.ToString());
        feature.Color.Should().Be("#aa0000");
        feature.Geometry.Should().BeOfType<GeoPoint>();
    }

    // ── Zoom and MaxCount (XD01-430) ──────────────────────────────────────────

    [Theory]
    [InlineData(null, new[] { "box", "pole" })]
    [InlineData(10, new[] { "box" })]
    [InlineData(15, new[] { "box", "pole" })]
    [InlineData(18, new[] { "pole" })]
    public async Task GetRenderableAsync_Zoom_LeavesOutTheTypesItHides(int? zoom, string[] expected)
    {
        _poles.MinZoom = 15;
        _boxes.MaxZoom = 16;
        Seed("pole", _poles, new GeoPoint(1, 1));
        Seed("box", _boxes, new GeoPoint(2, 2));

        var result = await NewSut().GetRenderableAsync(Whole, new RenderQueryOptions { Zoom = zoom }, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal(expected);
    }

    [Fact]
    public async Task GetRenderableAsync_MaxCount_TakesTheFirstByOrdinalId()
    {
        foreach (var id in new[] { "c", "a", "B", "b" })
            Seed(id, _poles, new GeoPoint(1, 1));

        var result = await NewSut().GetRenderableAsync(Whole, new RenderQueryOptions { MaxCount = 3 }, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("B", "a", "b");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRenderableAsync_NonPositiveMaxCount_ReturnsNothing(int maxCount)
    {
        Seed("pole", _poles, new GeoPoint(1, 1));

        var result = await NewSut().GetRenderableAsync(Whole, new RenderQueryOptions { MaxCount = maxCount }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public void RenderQueryOptions_ByDefault_AreCappedAtTheBoundsReadsLimit()
    {
        var options = new RenderQueryOptions();

        options.MaxCount.Should().Be(RenderQueryOptions.DefaultMaxCount).And.Be(10_000);
        options.Zoom.Should().BeNull();
    }

    [Fact]
    public async Task GetRenderableAsync_GeometrylessFeatures_AreDropped()
    {
        Seed("shape", _poles, new GeoPoint(1, 1));
        Seed("nothing", _poles, null);

        var result = await NewSut().GetRenderableAsync(Whole, new RenderQueryOptions(), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("shape");
    }

    [Fact]
    public async Task GetRenderableAsync_AssetTypeFilter_KeepsOnlyThatType()
    {
        Seed("pole", _poles, new GeoPoint(1, 1));
        Seed("box", _boxes, new GeoPoint(2, 2));

        var result = await NewSut().GetRenderableAsync(
            Whole, new RenderQueryOptions { AssetTypeId = _boxes.Id.ToString() }, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("box");
    }

    [Fact]
    public async Task GetRenderableAsync_AssetTypeFilter_IsOrdinal()
    {
        Seed("pole", _poles, new GeoPoint(1, 1));

        var result = await NewSut().GetRenderableAsync(
            Whole, new RenderQueryOptions { AssetTypeId = _poles.Id.ToString().ToUpperInvariant() }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRenderableAsync_UnknownTypeFilter_IsEmpty()
    {
        Seed("pole", _poles, new GeoPoint(1, 1));

        var result = await NewSut().GetRenderableAsync(
            Whole, new RenderQueryOptions { AssetTypeId = "nope" }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRenderableAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NewSut().GetRenderableAsync(Whole, new RenderQueryOptions(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public Task GetRenderableAsync_NullOptions_Throws()
    {
        var act = () => NewSut().GetRenderableAsync(Whole, null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetFeatureInfoAsync_ReturnsTheFeaturesProperties_OrderedByOrdinalId()
    {
        Seed("b", _poles, new GeoPoint(1, 1), "second");
        Seed("B", _poles, new GeoPoint(2, 2), "upper");
        Seed("a", _boxes, new GeoPoint(3, 3), "first");

        var result = await NewSut().GetFeatureInfoAsync(Whole, 10, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("B", "a", "b");
        result[2].Should().Be(new FeatureInfo("b", "name-b", _poles.Id.ToString(), "second"));
    }

    [Fact]
    public async Task GetFeatureInfoAsync_TakesAtMostMaxCount_OfTheOrderedFeatures()
    {
        Seed("c", _poles, new GeoPoint(1, 1));
        Seed("a", _poles, new GeoPoint(2, 2));
        Seed("b", _poles, new GeoPoint(3, 3));

        var result = await NewSut().GetFeatureInfoAsync(Whole, 2, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("a", "b");
    }

    [Fact]
    public async Task GetFeatureInfoAsync_GeometrylessFeatures_AreDropped()
    {
        Seed("shape", _poles, new GeoPoint(1, 1));
        Seed("nothing", _poles, null);

        var result = await NewSut().GetFeatureInfoAsync(Whole, 10, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("shape");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task GetFeatureInfoAsync_NonPositiveMaxCount_IsEmpty(int maxCount)
    {
        Seed("shape", _poles, new GeoPoint(1, 1));

        var result = await NewSut().GetFeatureInfoAsync(Whole, maxCount, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatureInfoAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NewSut().GetFeatureInfoAsync(Whole, 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The catalog's read takes the token (XD01-481) ─────────────────────────

    [Fact]
    public async Task GetRenderableAsync_HandsTheTokenToTheCatalogsBoundsRead()
    {
        Seed("in", _poles, new GeoPoint(1, 1));
        var catalog = new UnfilteredViewportAssetProvider(_catalog);
        using var cts = new CancellationTokenSource();

        var result = await new AssetProviderRenderQuery(catalog).GetRenderableAsync(Whole, new RenderQueryOptions(), cts.Token);

        result.Should().ContainSingle();
        catalog.BoundsToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetFeatureInfoAsync_HandsTheTokenToTheCatalogsBoundsRead()
    {
        Seed("in", _poles, new GeoPoint(1, 1));
        var catalog = new UnfilteredViewportAssetProvider(_catalog);
        using var cts = new CancellationTokenSource();

        var result = await new AssetProviderRenderQuery(catalog).GetFeatureInfoAsync(Whole, 10, cts.Token);

        result.Should().ContainSingle();
        catalog.BoundsToken.Should().Be(cts.Token);
    }
}
