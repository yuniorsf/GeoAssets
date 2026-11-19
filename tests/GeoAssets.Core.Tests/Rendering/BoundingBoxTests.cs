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
}
