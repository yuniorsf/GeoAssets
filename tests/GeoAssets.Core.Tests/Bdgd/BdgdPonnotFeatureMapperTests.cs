using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Bdgd;

public class BdgdPonnotFeatureMapperTests
{
    private static GeoFeature RawFeature(GeoGeometry? geometry, Dictionary<string, string>? attributes = null) => new()
    {
        Id = "raw-1",
        Geometry = geometry,
        Properties = new GeoFeatureProperties
        {
            Name = "raw",
            CustomAttributes = attributes ?? [],
        },
    };

    private static Dictionary<string, string> ValidAttributes(string codId = "PN0001") => new(StringComparer.Ordinal)
    {
        ["COD_ID"] = codId,
        ["TIP_EST"] = "Concreto DT",
        ["ALT_TOT"] = "11",
        ["CAP_TRC"] = "600",
    };

    [Fact]
    public void TryMap_AllRequiredFieldsPresent_ReturnsMappedPonnotFeature()
    {
        var raw = RawFeature(new GeoPoint(-46.633, -23.55), ValidAttributes());

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        reason.Should().BeNull();
        mapped.Should().NotBeNull();
        mapped!.Properties.AssetTypeId.Should().Be(BdgdAssetTypeIds.Ponnot.ToString());
        mapped.Properties.CustomAttributes.Should().BeEquivalentTo(ValidAttributes());
        mapped.Geometry.Should().BeOfType<GeoPoint>();
    }

    [Fact]
    public void TryMap_FieldNamesInDifferentCase_StillMatches()
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cod_id"] = "PN0002",
            ["Tip_Est"] = "Madeira",
            ["alt_tot"] = "9",
            ["cap_trc"] = "300",
        };
        var raw = RawFeature(new GeoPoint(-46.0, -23.0), attrs);

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        reason.Should().BeNull();
        mapped!.Properties.CustomAttributes["COD_ID"].Should().Be("PN0002");
        mapped.Properties.CustomAttributes["TIP_EST"].Should().Be("Madeira");
    }

    [Fact]
    public void TryMap_NonPointGeometry_SkipsWithReason()
    {
        var raw = RawFeature(new GeoLineString([(0, 0), (1, 1)]), ValidAttributes());

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        mapped.Should().BeNull();
        reason.Should().Contain("Point geometry");
    }

    [Fact]
    public void TryMap_MissingCodId_SkipsWithReason()
    {
        var attrs = ValidAttributes();
        attrs.Remove("COD_ID");
        var raw = RawFeature(new GeoPoint(-46.633, -23.55), attrs);

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        mapped.Should().BeNull();
        reason.Should().Contain("COD_ID");
    }

    [Theory]
    [InlineData("TIP_EST")]
    [InlineData("ALT_TOT")]
    [InlineData("CAP_TRC")]
    public void TryMap_MissingOtherRequiredField_SkipsWithReason(string missingField)
    {
        var attrs = ValidAttributes();
        attrs.Remove(missingField);
        var raw = RawFeature(new GeoPoint(-46.633, -23.55), attrs);

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        mapped.Should().BeNull();
        reason.Should().Contain(missingField);
    }

    [Fact]
    public void TryMap_NonNumericHeight_FailsSchemaValidation()
    {
        var attrs = ValidAttributes();
        attrs["ALT_TOT"] = "eleven meters";
        var raw = RawFeature(new GeoPoint(-46.633, -23.55), attrs);

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        mapped.Should().BeNull();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void TryMap_NullGeometry_SkipsWithReason()
    {
        var raw = RawFeature(null, ValidAttributes());

        var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);

        mapped.Should().BeNull();
        reason.Should().Contain("Point geometry");
    }
}
