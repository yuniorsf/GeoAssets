using FluentAssertions;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class EquatorialQgisMunicipalAttributeTests
{
    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find("equatorial-qgis-municipal");

    [Fact]
    public void Attributes_AreStringifiedInvariantlyByTheirValueTypeUnderTheirFieldNames()
    {
        var record = ProfileRecords.Record(
            "TRAFOS", TestData.Point(1, 2),
            ("INT_NUM", 123L), ("EQ_KVAN", 12.5), ("DT_X", new DateTime(2024, 3, 5, 10, 30, 0, DateTimeKind.Unspecified)), ("TB_IN_DESC", "ABC"));

        var attributes = ProfileRecords.Map(Profile, record).Element!.Attributes;

        attributes["INT_NUM"].Should().Be("123");
        attributes["EQ_KVAN"].Should().Be("12.5");
        attributes["DT_X"].Should().Be("2024-03-05");
        attributes["TB_IN_DESC"].Should().Be("ABC");
    }

    [Theory]
    [InlineData("PGS")]
    [InlineData("TRAFOS")]
    [InlineData("TRECHO_PRIMARIO")]
    [InlineData("TRECHO_SECUNDARIO")]
    public void Attributes_NullValuesAreOmittedOnEveryLayer(string layer)
    {
        var geometry = string.Equals(layer, "PGS", StringComparison.Ordinal) || string.Equals(layer, "TRAFOS", StringComparison.Ordinal)
            ? (NetTopologySuite.Geometries.Geometry)TestData.Point(1, 2)
            : TestData.Line((0, 0), (5, 5));
        var record = ProfileRecords.Record(layer, geometry, ("MUN_NOME", "PACO"), ("ENDERECO", null));

        ProfileRecords.Map(Profile, record).Element!.Attributes.Keys.Should().BeEquivalentTo(["MUN_NOME"]);
    }

    [Fact]
    public void Attributes_DoublesUseTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var record = ProfileRecords.Record("PGS", TestData.Point(1, 2), ("PGF_BARRAM", 1L), ("X", 12.5));

            ProfileRecords.Map(Profile, record).Element!.Attributes["X"].Should().Be("12.5");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Attributes_LargeIntegersKeepEveryDigit()
    {
        var record = ProfileRecords.Record("TRECHO_PRIMARIO", TestData.Line((0, 0), (5, 5)), ("MSLINK", 9007199254740993L));

        ProfileRecords.Map(Profile, record).Element!.Attributes["MSLINK"].Should().Be("9007199254740993");
    }
}
