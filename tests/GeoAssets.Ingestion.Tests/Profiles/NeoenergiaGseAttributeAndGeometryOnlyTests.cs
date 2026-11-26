using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class NeoenergiaGseAttributeAndGeometryOnlyTests
{
    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find("neoenergia-gse");

    [Fact]
    public void Attributes_AreStringifiedInvariantlyByTheirValueType()
    {
        var record = ProfileRecords.Record(
            "POSTE", TestData.Point(1, 2),
            ("NU_PG_ID", 123L), ("NU_ALTURA", 12.5), ("DT_INSTALACAO", new DateTime(2024, 3, 5, 10, 30, 0, DateTimeKind.Unspecified)), ("DE_BARRAMENTO", "ABC"));

        var attributes = ProfileRecords.Map(Profile, record).Element!.Attributes;

        attributes["NU_PG_ID"].Should().Be("123");
        attributes["NU_ALTURA"].Should().Be("12.5");
        attributes["DT_INSTALACAO"].Should().Be("2024-03-05");
        attributes["DE_BARRAMENTO"].Should().Be("ABC");
    }

    [Fact]
    public void Attributes_NullValuesAreOmitted()
    {
        var record = ProfileRecords.Record("POSTE", TestData.Point(1, 2), ("NU_PG_ID", 123L), ("DE_BARRAMENTO", null));

        ProfileRecords.Map(Profile, record).Element!.Attributes.Keys.Should().BeEquivalentTo(["NU_PG_ID"]);
    }

    [Fact]
    public void Attributes_DoublesUseTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var record = ProfileRecords.Record("POSTE", TestData.Point(1, 2), ("NU_PG_ID", 1L), ("NU_ALTURA", 12.5));

            ProfileRecords.Map(Profile, record).Element!.Attributes["NU_ALTURA"].Should().Be("12.5");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GeometryOnlyPole_KeyedByCoordinates()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("POSTE", TestData.Point(554271.85773680, 8566172.49068071))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates("neoenergia-gse", "PG", 554271.85773680, 8566172.49068071));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("Pole");
        element.References.Should().BeEmpty();
        element.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void GeometryOnlyInstalacao_IsAnMvDeviceSnappingToItsPole()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("INSTALACAO", TestData.Point(100.126, 200.004))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates("neoenergia-gse", "INST", 100.126, 200.004));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("MvDevice");
        var location = element.References.Single();
        location.Role.Should().Be(ReferenceRole.Location);
        location.TargetNamespace.Should().Be("PG");
        location.TargetKey.Should().BeNull();
        location.SnapPoint!.X.Should().Be(100.126);
        location.SnapPoint.Y.Should().Be(200.004);
    }

    [Theory]
    [InlineData("REDE_MT", "TRECHO_MT", "MvSegment")]
    [InlineData("REDE_BT", "TRECHO_BT", "LvSegment")]
    [InlineData("REDE_LT", "TRECHO_LT", "HvSegment")]
    public void GeometryOnlyNetworkLine_IsKeyedByItsCentroidWithSnapOnlyEndpoints(string layer, string ns, string assetClass)
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record(layer, TestData.Line((0, 0), (10, 0)))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates("neoenergia-gse", ns, 5, 0));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be(assetClass);
        element.References.Select(r => r.Role).Should().BeEquivalentTo([ReferenceRole.Start, ReferenceRole.End]);
        element.References.Should().OnlyContain(r => r.TargetKey == null && string.Equals(r.TargetNamespace, "PG", StringComparison.Ordinal) && r.SnapPoint != null);
        var start = element.References.Single(r => r.Role == ReferenceRole.Start);
        var end = element.References.Single(r => r.Role == ReferenceRole.End);
        (start.SnapPoint!.X, start.SnapPoint.Y).Should().Be((0, 0));
        (end.SnapPoint!.X, end.SnapPoint.Y).Should().Be((10, 0));
    }

    [Fact]
    public void FallbackAliases_TranslateTruncatedNamesSoMappingSeesFullNames()
    {
        Profile.FallbackFieldAliases("REDE_MT").Should().Contain("NU_PG_INIC", "NU_PG_INICIAL_ID");
    }
}
