using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Bdgd;

/// <summary>
/// XD01-224's AC: "Each BDGD layer (PONNOT, UNTRD, UNREMT, SEGCON, UC) round-trips its
/// documented attribute set through the data layer." Proven via the existing
/// <see cref="GeoJsonSerializer"/> (the actual persistence boundary a <see cref="GeoFeature"/>
/// crosses) plus <see cref="GeoFeatureAttributeValidator"/> (the existing attribute-schema
/// pipeline this ticket's schemas plug into — "or equivalent attribute-schema definitions" per
/// the ticket's own wording, in place of new EF Core entities).
/// </summary>
public class BdgdAssetTypesTests
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
    public void Ponnot_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COD_ID"] = "PN0001",
            ["TIP_EST"] = "Concreto DT",
            ["ALT_TOT"] = "11",
            ["CAP_TRC"] = "600",
        };
        var feature = FeatureWith(BdgdAssetTypeIds.Ponnot, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Ponnot, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Untrd_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COD_ID"] = "TD0001",
            ["POT_NOM"] = "75",
            ["TEN_PRI"] = "13800",
            ["TEN_SEC"] = "380",
        };
        var feature = FeatureWith(BdgdAssetTypeIds.Untrd, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Untrd, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Untrd_MissingRequiredField_FailsValidation()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["COD_ID"] = "TD0001" };

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Untrd, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Unremt_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["COD_ID"] = "UR0001" };
        var feature = FeatureWith(BdgdAssetTypeIds.Unremt, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Unremt, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Segcon_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COD_ID"] = "SC0001",
            ["FAS_CON"] = "ABC",
        };
        var geometry = new GeoLineString([(-46.633, -23.55), (-46.634, -23.551)]);
        var feature = FeatureWith(BdgdAssetTypeIds.Segcon, geometry, attributes);

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Segcon, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void Uc_DocumentedAttributeSet_ValidatesAndRoundTrips()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["COD_ID"] = "UC0001" };
        var feature = FeatureWith(BdgdAssetTypeIds.Uc, new GeoPoint(-46.633, -23.55), attributes);

        GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Uc, attributes).Should().BeEmpty();
        AssertRoundTrips(feature);
    }

    [Fact]
    public void All_ReturnsExactlyTheFiveDocumentedLayers() =>
        BdgdAssetTypes.All.Select(t => t.Name).Should().BeEquivalentTo(["PONNOT", "UNTRD", "UNREMT", "SEGCON", "UC"]);
}
