using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class LayerTests
{
    [Fact]
    public void Construction_AssignsDefaultId()
    {
        new Layer().Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Construction_TwoInstances_HaveDifferentIds()
    {
        new Layer().Id.Should().NotBe(new Layer().Id);
    }

    [Fact]
    public void Construction_HasExpectedDefaults()
    {
        var layer = new Layer();

        layer.Name.Should().BeEmpty();
        layer.GeometryType.Should().Be(GeometryType.Point);
        layer.Color.Should().Be("#3388ff");
        layer.Radius.Should().Be(8);
        layer.IconUrl.Should().BeEmpty();
        layer.Weight.Should().Be(3);
        layer.DashArray.Should().BeNull();
        layer.FillColor.Should().Be("#3388ff");
        layer.FillOpacity.Should().Be(0.2);
    }

    [Fact]
    public void PointStyle_RoundTripsThroughProperties()
    {
        var id = Guid.NewGuid();
        var layer = new Layer
        {
            Id = id,
            Name = "Water Towers",
            GeometryType = GeometryType.Point,
            Color = "#e74c3c",
            Radius = 10,
            IconUrl = "/icons/tower.png"
        };

        layer.Id.Should().Be(id);
        layer.Name.Should().Be("Water Towers");
        layer.GeometryType.Should().Be(GeometryType.Point);
        layer.Color.Should().Be("#e74c3c");
        layer.Radius.Should().Be(10);
        layer.IconUrl.Should().Be("/icons/tower.png");
    }

    [Fact]
    public void LineStyle_RoundTripsThroughProperties()
    {
        var layer = new Layer
        {
            GeometryType = GeometryType.LineString,
            Color = "#3498db",
            Weight = 5,
            DashArray = "5, 5"
        };

        layer.GeometryType.Should().Be(GeometryType.LineString);
        layer.Color.Should().Be("#3498db");
        layer.Weight.Should().Be(5);
        layer.DashArray.Should().Be("5, 5");
    }

    [Fact]
    public void PolygonStyle_RoundTripsThroughProperties()
    {
        var layer = new Layer
        {
            GeometryType = GeometryType.Polygon,
            Color = "#2ecc71",
            Weight = 2,
            FillColor = "#27ae60",
            FillOpacity = 0.4
        };

        layer.GeometryType.Should().Be(GeometryType.Polygon);
        layer.Color.Should().Be("#2ecc71");
        layer.Weight.Should().Be(2);
        layer.FillColor.Should().Be("#27ae60");
        layer.FillOpacity.Should().Be(0.4);
    }

    [Fact]
    public void TwoLayers_WithSameValues_AreNotReferenceEqual()
    {
        var id = Guid.NewGuid();
        var a = new Layer { Id = id, Name = "Same" };
        var b = new Layer { Id = id, Name = "Same" };

        a.Should().NotBeSameAs(b);
    }
}
