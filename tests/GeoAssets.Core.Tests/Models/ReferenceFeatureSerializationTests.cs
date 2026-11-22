using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// XD01-330: <see cref="ReferenceFeature"/> is the whole wire shape of another organization's features — exactly five
/// fields. Adding a sixth (an attribute, the topology, an organization id, an audit field) must fail here.
/// </summary>
public sealed class ReferenceFeatureSerializationTests
{
    private static readonly string[] TheFiveFields = ["id", "geometry", "label", "assetTypeId", "assetTypeName"];

    [Fact]
    public void Serializes_exactly_the_five_reference_fields()
    {
        var feature = new ReferenceFeature("pole-1", new GeoPoint(-38.5, -12.9), "Pole 1", "type-pole", "Pole");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(feature, GeoJsonSerializer.Options));

        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(TheFiveFields);
    }

    [Fact]
    public void The_record_declares_no_member_beyond_the_five_fields()
    {
        typeof(ReferenceFeature).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Id", "Geometry", "Label", "AssetTypeId", "AssetTypeName"]);
    }

    [Fact]
    public void Geometry_is_GeoJson_with_longitude_first()
    {
        var feature = new ReferenceFeature("pole-1", new GeoPoint(-38.5, -12.9), "Pole 1", "type-pole", "Pole");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(feature, GeoJsonSerializer.Options));

        var geometry = json.RootElement.GetProperty("geometry");
        geometry.GetProperty("type").GetString().Should().Be("Point");
        geometry.GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble()).Should().Equal(-38.5, -12.9);
    }

    [Fact]
    public void An_empty_asset_type_name_is_still_serialized()
    {
        var feature = new ReferenceFeature("pole-1", new GeoPoint(1, 2), "Pole 1", "unknown-type", string.Empty);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(feature, GeoJsonSerializer.Options));

        json.RootElement.GetProperty("assetTypeName").GetString().Should().BeEmpty();
    }
}
