using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

public class BoundingBoxTests
{
    [Fact]
    public void Around_BuildsTheBoxOfHalfSizeToleranceAroundThePoint()
    {
        var box = BoundingBox.Around(-38.5, -13.0, 0.01, 0.02);

        box.Should().Be(new BoundingBox(-38.51, -13.02, -38.49, -12.98));
    }

    [Fact]
    public void Around_ZeroTolerance_IsTheDegenerateBoxAtThePoint()
    {
        var box = BoundingBox.Around(10, 20, 0, 0);

        box.Should().Be(new BoundingBox(10, 20, 10, 20));
    }

    [Fact]
    public void Around_ToleranceInEachAxisIsIndependent()
    {
        var box = BoundingBox.Around(0, 0, 3, 1);

        (box.MaxLon - box.MinLon).Should().Be(6);
        (box.MaxLat - box.MinLat).Should().Be(2);
    }

    // ── Padded (XD01-429) ───────────────────────────────────────────────────

    [Fact]
    public void Padded_GrowsEachSideByTheRatioOfItsAxis()
    {
        var box = new BoundingBox(-40, -20, -38, -19).Padded(0.25);

        box.Should().Be(new BoundingBox(-40.5, -20.25, -37.5, -18.75));
    }

    [Fact]
    public void Padded_ClampsToTheWorld()
    {
        var box = new BoundingBox(-179, -89, 179, 89).Padded(0.5);

        box.Should().Be(new BoundingBox(-180, -90, 180, 90));
    }

    [Fact]
    public void Padded_ZeroRatio_OnlyClamps()
    {
        new BoundingBox(-10, -5, 10, 5).Padded(0).Should().Be(new BoundingBox(-10, -5, 10, 5));
        new BoundingBox(-200, -95, -100, 0).Padded(0).Should().Be(new BoundingBox(-180, -90, -100, 0));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Padded_RejectsANegativeOrNonFiniteRatio(double ratio)
    {
        var act = () => new BoundingBox(0, 0, 1, 1).Padded(ratio);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("ratio");
    }
}
