using FluentAssertions;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Isp;

/// <summary>
/// XD01-272's AC for the attribute-schema half of the model — the NTS↔EF Core round-trip half
/// is covered separately against a real PostGIS instance in
/// <c>GeoAssets.Provider.PostgreSQL.Tests.IspAssetTypesRoundTripTests</c>.
/// </summary>
public class IspAssetTypesTests
{
    private static GeoFeature FeatureWith(Guid assetTypeId, GeoGeometry geometry, Dictionary<string, string> attributes) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = geometry,
        Properties = new GeoFeatureProperties
        {
            Name = "test",
            AssetTypeId = assetTypeId.ToString(),
            CustomAttributes = attributes,
        },
    };

    private static void AssertRoundTrips(GeoFeature feature)
    {
        var json = GeoJsonSerializer.SerializeFeature(feature);
        var roundTripped = GeoJsonSerializer.DeserializeFeature(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.Properties.CustomAttributes.Should().BeEquivalentTo(feature.Properties.CustomAttributes);
    }

    [Fact]
    public void Cto_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "16",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "12",
        };
        var feature = FeatureWith(IspAssetTypeIds.Cto, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Cto, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Cto_MissingOccupiedPorts_FailsValidation()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "16",
        };

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Cto, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Ceo_UnrestrictedSchema_AnyAttributesValidate()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = "roadside pedestal" };

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Ceo, attributes).Should().BeEmpty();
    }

    [Fact]
    public void OpticalCable_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fiberCount"] = "48",
            ["cableType"] = "Duct",
        };
        var geometry = new GeoLineString([(-46.633, -23.55), (-46.634, -23.551)]);
        var feature = FeatureWith(IspAssetTypeIds.OpticalCable, geometry, attributes);

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.OpticalCable, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void OpticalCable_InvalidCableType_FailsValidation()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fiberCount"] = "48",
            ["cableType"] = "Submarine",
        };

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.OpticalCable, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Pto_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["parentCtoFeatureId"] = "cto-1" };
        var feature = FeatureWith(IspAssetTypeIds.Pto, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Pto, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Pto_MissingParentCto_FailsValidation() =>
        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Pto, new Dictionary<string, string>(StringComparer.Ordinal)).Should().NotBeEmpty();

    [Fact]
    public void Ont_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fsan"] = "HWTC12345678",
            ["macAddress"] = "AA:BB:CC:DD:EE:FF",
            ["parentPtoFeatureId"] = "pto-1",
        };
        var feature = FeatureWith(IspAssetTypeIds.Ont, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Ont, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Ont_InvalidMacAddress_FailsValidation()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fsan"] = "HWTC12345678",
            ["macAddress"] = "not-a-mac",
            ["parentPtoFeatureId"] = "pto-1",
        };

        GeoFeatureAttributeValidator.Validate(IspAssetTypes.Ont, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void All_ReturnsExactlyTheFiveDocumentedLayers() =>
        IspAssetTypes.All.Select(t => t.Name).Should().BeEquivalentTo(["CTO", "CEO", "Optical Cable", "PTO", "ONT"]);
}
