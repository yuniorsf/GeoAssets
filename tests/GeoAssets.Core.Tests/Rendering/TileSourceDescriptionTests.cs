using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-459 — <see cref="TileSourceDescription"/>: what a keyed tile source holds, always valid.</summary>
public sealed class TileSourceDescriptionTests
{
    [Fact]
    public void AValidDescription_KeepsItsValues()
    {
        var description = new TileSourceDescription("b45e", ["roads", "water"], 2, 14, new BoundingBox(-50, -30, -30, -10));

        description.Version.Should().Be("b45e");
        description.LayerIds.Should().Equal("roads", "water");
        (description.MinZoom, description.MaxZoom).Should().Be((2, 14));
        description.Bounds.Should().Be(new BoundingBox(-50, -30, -30, -10));
        new TileSourceDescription("v", ["a"], 0, TileAddress.MaxZoom).Bounds.Should().BeNull();
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("..")]
    public void AVersionThatIsNotATileKeySegment_IsRefused(string version)
    {
        var create = () => new TileSourceDescription(version, ["a"], 0, 1);

        create.Should().Throw<ArgumentException>().WithParameterName(nameof(version));
    }

    [Fact]
    public void NoLayer_ARepeatedOne_OrOneThatIsNotATileKeySegment_IsRefused()
    {
        ((Func<TileSourceDescription>)(() => new TileSourceDescription("v", [], 0, 1))).Should().Throw<ArgumentException>().WithParameterName("layerIds");
        ((Func<TileSourceDescription>)(() => new TileSourceDescription("v", ["a", "a"], 0, 1))).Should().Throw<ArgumentException>().WithParameterName("layerIds");
        ((Func<TileSourceDescription>)(() => new TileSourceDescription("v", ["a/b"], 0, 1))).Should().Throw<ArgumentException>().WithParameterName("layerIds");
        ((Func<TileSourceDescription>)(() => new TileSourceDescription("v", null!, 0, 1))).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-1, 4, "minZoom")]
    [InlineData(TileAddress.MaxZoom + 1, TileAddress.MaxZoom + 1, "minZoom")]
    [InlineData(5, 4, "maxZoom")]
    [InlineData(0, TileAddress.MaxZoom + 1, "maxZoom")]
    public void AZoomRangeOutsideThePyramidOrReversed_IsRefused(int minZoom, int maxZoom, string parameter)
    {
        var create = () => new TileSourceDescription("v", ["a"], minZoom, maxZoom);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [Theory]
    [InlineData(10, -10, -10, 10)]
    [InlineData(-10, 10, 10, -10)]
    [InlineData(-181, -10, 10, 10)]
    [InlineData(-10, -91, 10, 10)]
    [InlineData(double.NaN, -10, 10, 10)]
    public void BoundsThatAreNotAnExtent_AreRefused(double minLon, double minLat, double maxLon, double maxLat)
    {
        var create = () => new TileSourceDescription("v", ["a"], 0, 1, new BoundingBox(minLon, minLat, maxLon, maxLat));

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("bounds");
    }
}
