using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class AssetTypeTests
{
    [Fact]
    public void IsProtected_DefaultsToFalse()
    {
        new AssetType().IsProtected.Should().BeFalse();
    }

    [Fact]
    public void AllowedGeometryType_DefaultsToNull()
    {
        new AssetType().AllowedGeometryType.Should().BeNull();
    }

    [Fact]
    public void DefaultLayerId_DefaultsToNull()
    {
        new AssetType().DefaultLayerId.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(AssetType.Point))]
    [InlineData(nameof(AssetType.Line))]
    [InlineData(nameof(AssetType.Area))]
    public void GenericDefaults_AreProtected(string name)
    {
        var assetType = name switch
        {
            nameof(AssetType.Point) => AssetType.Point,
            nameof(AssetType.Line) => AssetType.Line,
            _ => AssetType.Area
        };

        assetType.IsProtected.Should().BeTrue();
        assetType.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void BuiltInType_CanBeUnprotected()
    {
        var assetType = new AssetType { IsBuiltIn = true, IsProtected = false };

        assetType.IsBuiltIn.Should().BeTrue();
        assetType.IsProtected.Should().BeFalse();
    }

    [Fact]
    public void AllowedGeometryType_CanBeSet()
    {
        var assetType = new AssetType { AllowedGeometryType = GeometryType.Polygon };

        assetType.AllowedGeometryType.Should().Be(GeometryType.Polygon);
    }
}
