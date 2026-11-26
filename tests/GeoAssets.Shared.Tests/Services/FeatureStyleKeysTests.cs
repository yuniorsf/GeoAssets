using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>XD01-429 — <see cref="FeatureStyleKeys"/> reads what a feature's style depends on, and nothing else.</summary>
public sealed class FeatureStyleKeysTests
{
    [Fact]
    public void Read_KeepsIdTypeLayerAndAttributes_AndNoGeometry()
    {
        var keys = FeatureStyleKeys.Read("""
            [{"type":"Feature","id":"a","geometry":{"type":"Point","coordinates":[1,2]},
              "properties":{"name":"A","assetTypeId":"t1","layerId":"l1","customAttributes":{"material":"steel","voltageKv":13.8,"spare":true}}}]
            """);

        var key = keys.Should().ContainSingle().Subject;
        key.Id.Should().Be("a");
        key.Geometry.Should().BeNull();
        key.Properties.AssetTypeId.Should().Be("t1");
        key.Properties.LayerId.Should().Be("l1");
        key.Properties.Name.Should().BeEmpty("only what a style depends on is read");
        key.Properties.CustomAttributes.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["material"] = "steel",
            ["voltageKv"] = "13.8",
            ["spare"] = "true",
        }, "a non-string attribute keeps its JSON text, which LayerResolver compares invariantly");
    }

    [Fact]
    public void Read_ANumericId_KeepsItsText()
    {
        using var doc = JsonDocument.Parse("""{"id":42,"properties":{"assetTypeId":"t"}}""");

        FeatureStyleKeys.Read(doc.RootElement).Id.Should().Be("42");
    }

    [Theory]
    [InlineData("""{"id":"a"}""")]
    [InlineData("""{"id":"a","properties":null}""")]
    [InlineData("""{"id":"a","properties":{"assetTypeId":null,"customAttributes":[]}}""")]
    public void Read_MissingProperties_KeepTheModelDefaults(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var key = FeatureStyleKeys.Read(doc.RootElement);

        key.Id.Should().Be("a");
        key.Properties.AssetTypeId.Should().Be(AssetType.Point.Id.ToString(), "as a full deserialization would default it");
        key.Properties.LayerId.Should().BeEmpty();
        key.Properties.CustomAttributes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("""["a"]""")]
    public void Read_ANonObjectFeature_HasNoId(string json)
    {
        FeatureStyleKeys.Read(json).Should().ContainSingle().Which.Id.Should().BeEmpty();
    }

    [Fact]
    public void Read_ANonArray_HasNoKeys()
    {
        FeatureStyleKeys.Read("""{"id":"a"}""").Should().BeEmpty();
    }

    [Fact]
    public void Read_RejectsNull()
    {
        var act = () => FeatureStyleKeys.Read((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
