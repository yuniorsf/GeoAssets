using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class EquatorialQgisMunicipalPointMappingTests
{
    private const string ProfileId = "equatorial-qgis-municipal";

    private static readonly ISourceProfile Profile = BuiltInProfiles.CreateRegistry().Find(ProfileId);

    private static CanonicalElement MapPg(object? barram, double x = 554271.85, double y = 8566172.49) =>
        ProfileRecords.Map(
            Profile,
            ProfileRecords.Record(
                "PGS", TestData.Point(x, y),
                ("MUN_NOME", "PACO DO LUMIAR"), ("PGF_BARRAM", barram), ("ENDERECO", "RUA A"), ("X", x), ("Y", y))).Element!;

    private static CanonicalElement MapTrafo(object? intNum, string? description = "Transformador", double x = 100.126, double y = 200.004) =>
        ProfileRecords.Map(
            Profile,
            ProfileRecords.Record(
                "TRAFOS", TestData.Point(x, y),
                ("MUN_NOME", "PACO DO LUMIAR"), ("SE_CODIGO", "SE1"), ("AL_CODIGO", "AL1"), ("INT_NUM", intNum),
                ("EQ_KVAN", 75.0), ("TB_IN_DESC", description), ("TRAFO_AL", "X"))).Element!;

    [Fact]
    public void Pgs_IsAPoleKeyedByItsPointCoordinates()
    {
        var element = MapPg(12345L, 554271.85773680, 8566172.49068071);

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "PG", 554271.85773680, 8566172.49068071));
        element.Kind.Should().Be(ElementKind.Structure);
        element.AssetClass.Should().Be("Pole");
        element.Completeness.Should().Be(Completeness.Full);
        element.Scope.Should().Be(ProfileRecords.Scope);
        element.References.Should().BeEmpty();
    }

    [Fact]
    public void Pgs_KeyIgnoresTheXAndYAttributesAndUsesTheGeometry()
    {
        var record = ProfileRecords.Record("PGS", TestData.Point(10.001, 20.002), ("PGF_BARRAM", 1L), ("X", 999.0), ("Y", 999.0));

        ProfileRecords.Map(Profile, record).Element!.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "PG", 10.001, 20.002));
    }

    [Theory]
    [InlineData(55L, "Poste 55")]
    [InlineData("P-12", "Poste P-12")]
    [InlineData("  P-12  ", "Poste P-12")]
    public void Pgs_NameIsPosteFollowedByTheBarramento(object barram, string expected)
    {
        MapPg(barram).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pgs_BlankBarramentoGivesNoName(string? barram)
    {
        MapPg(barram).Name.Should().BeNull();
    }

    [Fact]
    public void Pgs_GeometryOnlyRecord_IsAPoleKeyedByCoordinatesWithoutNameOrAttributes()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("PGS", TestData.Point(1.5, 2.5))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "PG", 1.5, 2.5));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("Pole");
        element.Name.Should().BeNull();
        element.Attributes.Should().BeEmpty();
        element.References.Should().BeEmpty();
    }

    [Fact]
    public void Trafos_KeyIsTheTrimmedIntNumInTheInstNamespace()
    {
        MapTrafo(123456L).Key.Should().Be(new SourceKey(ProfileId, "INST", "123456"));
        MapTrafo("  A-77 ").Key.Should().Be(new SourceKey(ProfileId, "INST", "A-77"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Trafos_WithoutAnIntNum_IsKeyedByCoordinates(string? intNum)
    {
        MapTrafo(intNum, x: 100.126, y: 200.004).Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "INST", 100.126, 200.004));
    }

    [Fact]
    public void Trafos_NameIsTrafoFollowedByIntNum()
    {
        var element = MapTrafo(4321L);

        element.Name.Should().Be("Trafo 4321");
        element.Kind.Should().Be(ElementKind.Device);
        element.Scope.Should().Be(ProfileRecords.Scope);
    }

    [Theory]
    [InlineData("Estação Transformadora")]
    [InlineData("ESTAÇÃO TRANSFORMADORA")]
    [InlineData("Estacao Transformadora")]
    [InlineData("estacao transformadora de distribuicao")]
    [InlineData("Transformadora")]
    [InlineData("TRANSFORMADORA")]
    [InlineData("Transformádora")]
    public void Trafos_DescriptionContainingTransformadora_IsATransformerRegardlessOfCaseAndAccents(string description)
    {
        MapTrafo(1L, description).AssetClass.Should().Be("Transformer");
    }

    [Theory]
    [InlineData("Religador")]
    [InlineData("Transformador")]
    [InlineData("Chave Fusível")]
    [InlineData("")]
    [InlineData(null)]
    public void Trafos_AnyOtherDescription_IsAnMvDevice(string? description)
    {
        MapTrafo(1L, description).AssetClass.Should().Be("MvDevice");
    }

    [Fact]
    public void Trafos_LocatesItselfOnAPoleBySnappingFromTheDevicePoint()
    {
        var element = MapTrafo(1L);

        var location = element.References.Single();
        location.Role.Should().Be(ReferenceRole.Location);
        location.TargetNamespace.Should().Be("PG");
        location.TargetKey.Should().BeNull();
        location.GroupKey.Should().BeNull();
        (location.SnapPoint!.X, location.SnapPoint.Y).Should().Be((100.126, 200.004));
    }

    [Fact]
    public void Trafos_GeometryOnlyRecord_IsAnMvDeviceKeyedByCoordinatesSnappingToItsPole()
    {
        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("TRAFOS", TestData.Point(100.126, 200.004))).Element!;

        element.Key.Should().Be(SourceKey.ForCoordinates(ProfileId, "INST", 100.126, 200.004));
        element.Completeness.Should().Be(Completeness.GeometryOnly);
        element.AssetClass.Should().Be("MvDevice");
        var location = element.References.Single();
        location.Role.Should().Be(ReferenceRole.Location);
        location.TargetNamespace.Should().Be("PG");
        location.TargetKey.Should().BeNull();
        (location.SnapPoint!.X, location.SnapPoint.Y).Should().Be((100.126, 200.004));
    }

    [Fact]
    public void Trafos_FromAMultiPoint_UsesItsFirstPointForTheSnapReference()
    {
        var geometry = TestData.MultiPoint(TestData.Point(3, 4), TestData.Point(5, 6));

        var element = ProfileRecords.Map(Profile, ProfileRecords.Record("TRAFOS", geometry, ("INT_NUM", 9L))).Element!;

        (element.References.Single().SnapPoint!.X, element.References.Single().SnapPoint!.Y).Should().Be((3, 4));
    }
}
