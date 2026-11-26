using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class NeoenergiaGseClassificationTests
{
    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find("neoenergia-gse");

    private static CanonicalElement MapPole(object? tipo, long id = 123)
    {
        var record = ProfileRecords.Record("POSTE", TestData.Point(554000.5, 8566000.5), ("NU_PG_ID", id), ("CD_TIPO_PG", tipo));
        return ProfileRecords.Map(Profile, record).Element!;
    }

    private static CanonicalElement MapDevice(string? sigla, long id = 77, string? placa = null)
    {
        var record = ProfileRecords.Record(
            "INSTALACAO", TestData.Point(554010, 8566010),
            ("NU_INSTALACAO_MT_ID", id), ("DE_SIGLA_1", sigla), ("DE_PLACA", placa), ("NU_PG_ID", 123L), ("NU_NO", 1L));
        return ProfileRecords.Map(Profile, record).Element!;
    }

    [Fact]
    public void Pole_KeyKindScopeAndNameFollowTheNuPgId()
    {
        var element = MapPole(1L);

        element.Key.Should().Be(new SourceKey("neoenergia-gse", "PG", "123"));
        element.Kind.Should().Be(ElementKind.Structure);
        element.AssetClass.Should().Be("Pole");
        element.Name.Should().Be("Poste 123");
        element.Scope.Should().Be(ProfileRecords.Scope);
        element.Completeness.Should().Be(Completeness.Full);
        element.References.Should().BeEmpty();
    }

    [Fact]
    public void Pole_KeepsTheRecordGeometry()
    {
        var element = MapPole(1L);

        element.Geometry.Coordinate.X.Should().Be(554000.5);
        element.Geometry.Coordinate.Y.Should().Be(8566000.5);
    }

    [Theory]
    [InlineData(90L)]
    [InlineData(91L)]
    public void Poste_TipoPg90Or91_IsAGeographicPoint(long tipo)
    {
        var element = MapPole(tipo);

        element.AssetClass.Should().Be("GeographicPoint");
        element.Name.Should().Be("Ponto 123");
        element.Key.Should().Be(new SourceKey("neoenergia-gse", "PG", "123"));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(89L)]
    [InlineData(92L)]
    [InlineData(null)]
    public void Poste_OtherTipoPg_IsAPole(long? tipo)
    {
        MapPole(tipo).AssetClass.Should().Be("Pole");
    }

    [Theory]
    [InlineData("TF", "Transformer")]
    [InlineData("TF1", "Transformer")]
    [InlineData("TFM", "Transformer")]
    [InlineData("AUTRF", "Transformer")]
    [InlineData("DJ", "Breaker")]
    [InlineData("RELIG", "Recloser")]
    [InlineData("BC", "CapacitorBank")]
    [InlineData("BC3", "CapacitorBank")]
    [InlineData("REGUL", "VoltageRegulator")]
    [InlineData("CUBIL", "Cubicle")]
    [InlineData("CH", "Switch")]
    [InlineData("CH2", "Switch")]
    [InlineData("CF", "Switch")]
    [InlineData("CF5", "Switch")]
    [InlineData("SEC", "Switch")]
    [InlineData("SEC1", "Switch")]
    [InlineData("ZZ", "MvDevice")]
    [InlineData("DJX", "MvDevice")]
    [InlineData("RELIGX", "MvDevice")]
    [InlineData("AUTRFX", "MvDevice")]
    [InlineData("REGULX", "MvDevice")]
    [InlineData(null, "MvDevice")]
    public void Instalacao_DeSigla1_DecidesTheAssetClass(string? sigla, string expected)
    {
        MapDevice(sigla).AssetClass.Should().Be(expected);
    }

    [Fact]
    public void Instalacao_KeyKindAndNameUseTheInstallationIdWhenThereIsNoPlate()
    {
        var element = MapDevice("DJ", id: 77);

        element.Key.Should().Be(new SourceKey("neoenergia-gse", "INST", "77"));
        element.Kind.Should().Be(ElementKind.Device);
        element.Name.Should().Be("DJ 77");
    }

    [Fact]
    public void Instalacao_NameUsesThePlateWhenPresent()
    {
        MapDevice("TF1", id: 77, placa: "TR-ABC").Name.Should().Be("TF1 TR-ABC");
    }

    [Fact]
    public void Instalacao_LocationReferenceTargetsThePoleAndSnapsToTheDevicePoint()
    {
        var element = MapDevice("DJ");

        var location = element.References.Single(r => r.Role == ReferenceRole.Location);
        location.TargetNamespace.Should().Be("PG");
        location.TargetKey.Should().Be("123");
        location.LocalNode.Should().Be(1);
        location.SnapPoint.Should().NotBeNull();
        location.SnapPoint!.X.Should().Be(554010);
        location.SnapPoint.Y.Should().Be(8566010);
    }

    [Fact]
    public void Instalacao_ConnectionReferenceTargetsTheSecondPoleWithItsNode()
    {
        var record = ProfileRecords.Record(
            "INSTALACAO", TestData.Point(1, 2),
            ("NU_INSTALACAO_MT_ID", 77L), ("DE_SIGLA_1", "CH"), ("NU_PG_ID", 123L), ("NU_NO", 1L), ("NU_PG_P_ID", 456L), ("NU_NO_P", 2L));

        var element = ProfileRecords.Map(Profile, record).Element!;

        var connection = element.References.Single(r => r.Role == ReferenceRole.Connection);
        connection.TargetNamespace.Should().Be("PG");
        connection.TargetKey.Should().Be("456");
        connection.LocalNode.Should().Be(2);
    }
}
