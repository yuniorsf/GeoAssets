using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class GeoJsonSerializerTests
{
    private const string PointJson        = """{"type":"Point","coordinates":[1.0,2.0]}""";
    private const string LineStringJson   = """{"type":"LineString","coordinates":[[0.0,0.0],[1.0,1.0]]}""";
    private const string PolygonJson      = """{"type":"Polygon","coordinates":[[[0.0,0.0],[1.0,0.0],[1.0,1.0],[0.0,0.0]]]}""";
    private const string MultiPolygonJson = """{"type":"MultiPolygon","coordinates":[[[[0.0,0.0],[1.0,0.0],[1.0,1.0],[0.0,0.0]]]]}""";

    // ── Serialize / Deserialize (GeoFeatureCollection) ────────────────────────

    [Fact]
    public void Serialize_EmptyCollection_ContainsFeatureCollectionType()
    {
        GeoJsonSerializer.Serialize(new GeoFeatureCollection())
            .Should().Contain("\"type\": \"FeatureCollection\"");
    }

    [Fact]
    public void Serialize_ProducesIndentedOutput()
    {
        GeoJsonSerializer.Serialize(new GeoFeatureCollection())
            .Should().Contain("\n");
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsCollectionWithFeatures()
    {
        var original = new GeoFeatureCollection();
        original.Features.Add(new GeoFeature { Id = "f1" });

        var json = GeoJsonSerializer.Serialize(original);
        var result = GeoJsonSerializer.Deserialize(json);

        result.Should().NotBeNull();
        result!.Features.Should().ContainSingle().Which.Id.Should().Be("f1");
    }

    [Fact]
    public void Deserialize_NullLiteral_ReturnsNull()
    {
        GeoJsonSerializer.Deserialize("null").Should().BeNull();
    }

    // ── SerializeFeature / DeserializeFeature ─────────────────────────────────

    [Fact]
    public void SerializeFeature_ValidFeature_ContainsFeatureType()
    {
        GeoJsonSerializer.SerializeFeature(new GeoFeature { Id = "x" })
            .Should().Contain("\"type\": \"Feature\"");
    }

    [Fact]
    public void DeserializeFeature_ValidJson_ReturnsFeatureWithMatchingId()
    {
        var json = GeoJsonSerializer.SerializeFeature(new GeoFeature { Id = "abc" });
        GeoJsonSerializer.DeserializeFeature(json)!.Id.Should().Be("abc");
    }

    [Fact]
    public void DeserializeFeature_NullLiteral_ReturnsNull()
    {
        GeoJsonSerializer.DeserializeFeature("null").Should().BeNull();
    }

    // ── DeserializeGeometry ───────────────────────────────────────────────────

    [Fact]
    public void DeserializeGeometry_NullLiteral_ReturnsNull()
    {
        GeoJsonSerializer.DeserializeGeometry("null").Should().BeNull();
    }

    // ── GetOptions / GetCompactOptions ────────────────────────────────────────

    [Fact]
    public void GetOptions_WriteIndented_IsTrue()
    {
        GeoJsonSerializer.GetOptions().WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void GetCompactOptions_WriteIndented_IsFalse()
    {
        GeoJsonSerializer.GetCompactOptions().WriteIndented.Should().BeFalse();
    }

    [Fact]
    public void GetCompactOptions_SerializedOutput_ContainsNoNewlines()
    {
        var json = JsonSerializer.Serialize(new GeoFeature { Id = "x" }, GeoJsonSerializer.GetCompactOptions());
        json.Should().NotContain("\n");
    }

    // ── GeoGeometryConverter.Read ─────────────────────────────────────────────

    [Fact]
    public void Read_NoTypeProperty_ReturnsNull()
    {
        GeoJsonSerializer.DeserializeGeometry("""{"coordinates":[0.0,0.0]}""")
            .Should().BeNull();
    }

    [Fact]
    public void Read_NullTypeValue_ReturnsNull()
    {
        GeoJsonSerializer.DeserializeGeometry("""{"type":null,"coordinates":[0.0,0.0]}""")
            .Should().BeNull();
    }

    [Fact]
    public void Read_TypePoint_ReturnsGeoPoint()
    {
        GeoJsonSerializer.DeserializeGeometry(PointJson).Should().BeOfType<GeoPoint>();
    }

    [Fact]
    public void Read_TypePoint_CoordinatesPreserved()
    {
        var point = (GeoPoint)GeoJsonSerializer.DeserializeGeometry(PointJson)!;
        point.Longitude.Should().Be(1.0);
        point.Latitude.Should().Be(2.0);
    }

    [Fact]
    public void Read_TypeLineString_ReturnsGeoLineString()
    {
        GeoJsonSerializer.DeserializeGeometry(LineStringJson).Should().BeOfType<GeoLineString>();
    }

    [Fact]
    public void Read_TypePolygon_ReturnsGeoPolygon()
    {
        GeoJsonSerializer.DeserializeGeometry(PolygonJson).Should().BeOfType<GeoPolygon>();
    }

    [Fact]
    public void Read_UnknownType_ReturnsGeoRawGeometryWithCorrectType()
    {
        var result = GeoJsonSerializer.DeserializeGeometry(MultiPolygonJson);
        result.Should().BeOfType<GeoRawGeometry>().Which.Type.Should().Be("MultiPolygon");
    }

    // ── GeoGeometryConverter.Write ────────────────────────────────────────────

    [Fact]
    public void Write_GeoPoint_ProducesPointTypeInJson()
    {
        var feature = new GeoFeature { Geometry = new GeoPoint(1.0, 2.0) };
        GeoJsonSerializer.SerializeFeature(feature).Should().Contain("\"type\": \"Point\"");
    }

    [Fact]
    public void Write_GeoPoint_RoundTripsCoordinates()
    {
        var feature = new GeoFeature { Id = "p", Geometry = new GeoPoint(3.5, 7.1) };
        var result = GeoJsonSerializer.DeserializeFeature(GeoJsonSerializer.SerializeFeature(feature))!;
        result.Geometry.Should().BeOfType<GeoPoint>()
            .Which.Longitude.Should().BeApproximately(3.5, 1e-9);
    }

    [Fact]
    public void Write_GeoLineString_ProducesLineStringTypeInJson()
    {
        var feature = new GeoFeature { Geometry = new GeoLineString([(0.0, 0.0), (1.0, 1.0)]) };
        GeoJsonSerializer.SerializeFeature(feature).Should().Contain("\"type\": \"LineString\"");
    }

    [Fact]
    public void Write_GeoPolygon_ProducesPolygonTypeInJson()
    {
        var feature = new GeoFeature
        {
            Geometry = new GeoPolygon([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 0.0)])
        };
        GeoJsonSerializer.SerializeFeature(feature).Should().Contain("\"type\": \"Polygon\"");
    }

    [Fact]
    public void Write_GeoRawGeometry_ContainsRawTypeInJson()
    {
        var feature = new GeoFeature { Geometry = new GeoRawGeometry("MultiPolygon", MultiPolygonJson) };
        GeoJsonSerializer.SerializeFeature(feature).Should().Contain("\"MultiPolygon\"");
    }

    [Fact]
    public void Write_GeoRawGeometry_RoundTripPreservesType()
    {
        var feature = new GeoFeature { Id = "r", Geometry = new GeoRawGeometry("MultiPolygon", MultiPolygonJson) };
        var json = GeoJsonSerializer.SerializeFeature(feature);
        GeoJsonSerializer.DeserializeFeature(json)!.Geometry
            .Should().BeOfType<GeoRawGeometry>().Which.Type.Should().Be("MultiPolygon");
    }
}
