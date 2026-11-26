using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class NeoenergiaGseProfileCatalogTests
{
    private static ISourceProfile Profile() => BuiltInProfiles.CreateRegistry().Find("neoenergia-gse");

    private static AssetClassDefinition Class(string name, string role, string typeId, string display, GeometryType geometry, string color, params string[] reuse) =>
        new(name, role, Guid.Parse(typeId), display, geometry, color, reuse);

    [Fact]
    public void Registry_ListsAndFindsTheNeoenergiaGseProfile()
    {
        var registry = BuiltInProfiles.CreateRegistry();

        registry.ProfileIds.Should().Contain("neoenergia-gse");
        registry.Find("neoenergia-gse").Id.Should().Be("neoenergia-gse");
    }

    [Fact]
    public void Layers_AreTheFiveCoelbaLayersWithTheirKindOrderAndIndexes()
    {
        var layers = Profile().Layers.ToDictionary(l => l.Name, StringComparer.Ordinal);

        layers.Keys.Should().BeEquivalentTo(["REDE_BT", "REDE_MT", "REDE_LT", "INSTALACAO", "POSTE"]);
        layers.Values.Should().OnlyContain(l => string.Equals(l.Name, l.FileStem, StringComparison.Ordinal));
        layers.Values.Should().OnlyContain(l => l.SnapToleranceMeters == 1.0 && l.MaxEndpointDistanceMeters == 5.0);
        layers.Values.Should().OnlyContain(l => l.DrawingOffsetX == 0 && l.DrawingOffsetY == 0);

        layers["REDE_BT"].Kind.Should().Be(ElementKind.Segment);
        layers["REDE_BT"].ProcessingOrder.Should().Be(10);
        layers["REDE_MT"].ProcessingOrder.Should().Be(20);
        layers["REDE_LT"].ProcessingOrder.Should().Be(20);
        layers["INSTALACAO"].Kind.Should().Be(ElementKind.Device);
        layers["INSTALACAO"].ProcessingOrder.Should().Be(30);
        layers["POSTE"].Kind.Should().Be(ElementKind.Structure);
        layers["POSTE"].ProcessingOrder.Should().Be(40);
    }

    [Fact]
    public void Layers_OnlyPosteIsASnapTarget()
    {
        Profile().Layers.Where(l => l.SnapTarget).Select(l => l.Name).Should().Equal("POSTE");
    }

    [Fact]
    public void Layers_IndexKeysAreThePgInstAndChaveNamespaces()
    {
        var layers = Profile().Layers.ToDictionary(l => l.Name, StringComparer.Ordinal);

        layers["POSTE"].IndexKeys.Should().Equal(new IndexKeyDefinition("PG", "NU_PG_ID"));
        layers["INSTALACAO"].IndexKeys.Should().BeEquivalentTo(
            [new IndexKeyDefinition("INST", "NU_INSTALACAO_MT_ID"), new IndexKeyDefinition("CHAVE", "NU_CHAVE_MT_ID")]);
        layers["REDE_BT"].IndexKeys.Should().BeEmpty();
        layers["REDE_MT"].IndexKeys.Should().BeEmpty();
        layers["REDE_LT"].IndexKeys.Should().BeEmpty();
    }

    [Fact]
    public void AssetClasses_AreTheFifteenElectricClassesWithFrozenTypeIds()
    {
        AssetClassDefinition[] expected =
        [
            Class("Pole", "electric:pole", "5b3d5dde-182f-4725-bd56-e35846c77324", "Poste", GeometryType.Point, "#8d6e63", "occupiable-support"),
            Class("GeographicPoint", "electric:geographic-point", "7e0d0562-11c5-4b20-be50-364b6b894113", "Ponto geográfico", GeometryType.Point, "#9e9e9e"),
            Class("Tower", "electric:tower", "2d369700-1081-4568-8ea5-88cdd78ece31", "Torre", GeometryType.Point, "#6d4c41"),
            Class("Transformer", "electric:transformer", "81f4acdf-6d53-490d-b894-a9b41a71d18c", "Transformador", GeometryType.Point, "#f9a825"),
            Class("Breaker", "electric:breaker", "61628e59-1dc2-4015-962d-f8350111aa12", "Disjuntor", GeometryType.Point, "#c62828"),
            Class("Switch", "electric:switch", "78c9f88d-2e9a-4b8b-b1a5-8840f5bcceac", "Chave", GeometryType.Point, "#1565c0"),
            Class("Recloser", "electric:recloser", "8a6b8277-d0f2-41a5-8d4f-749d987acd35", "Religador", GeometryType.Point, "#6a1b9a"),
            Class("CapacitorBank", "electric:capacitor-bank", "b4707f77-5d43-43af-8601-986561d9e769", "Banco de capacitores", GeometryType.Point, "#00838f"),
            Class("VoltageRegulator", "electric:voltage-regulator", "83105f64-01b7-41de-9082-7a38069a245f", "Regulador de tensão", GeometryType.Point, "#ef6c00"),
            Class("Cubicle", "electric:cubicle", "9addb5f2-bf26-47e0-ac28-659548fe78b1", "Cubículo", GeometryType.Point, "#4e342e"),
            Class("MvDevice", "electric:mv-device", "fd2237c0-be80-4a78-ae8f-6c157772c9d6", "Equipamento MT", GeometryType.Point, "#546e7a"),
            Class("MvSegment", "electric:mv-segment", "fd551c47-7a38-40e0-af03-63c9e93a5942", "Trecho MT", GeometryType.LineString, "#d32f2f"),
            Class("MvJumper", "electric:mv-jumper", "7a3e2e66-b959-41d1-86ad-3a1b26272152", "Jumper MT", GeometryType.LineString, "#e57373"),
            Class("LvSegment", "electric:lv-segment", "ba0efdf2-573c-4771-9868-12d3f93bf649", "Trecho BT", GeometryType.LineString, "#2e7d32"),
            Class("HvSegment", "electric:hv-segment", "70f64a14-97c1-41c1-b454-335290e2b790", "Trecho LT", GeometryType.LineString, "#4a148c"),
        ];

        Profile().AssetClasses.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AssetClasses_OnlyThePoleReusesAnExistingRole()
    {
        Profile().AssetClasses.Where(c => c.ReuseRoles.Count > 0).Select(c => c.Name).Should().Equal("Pole");
    }

    [Fact]
    public void AssetClasses_HaveUniqueNamesRolesAndTypeIds()
    {
        var classes = Profile().AssetClasses;

        classes.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        classes.Select(c => c.Role).Should().OnlyHaveUniqueItems();
        classes.Select(c => c.TypeId).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("POSTE", "NU_PG_ID", "NU_PG_ID")]
    [InlineData("POSTE", "CD_INDD_MA", "CD_INDD_MATERIAL_PG")]
    [InlineData("INSTALACAO", "NU_INSTALA", "NU_INSTALACAO_MT_ID")]
    [InlineData("REDE_MT", "NU_PG_INIC", "NU_PG_INICIAL_ID")]
    [InlineData("REDE_MT", "NU_PG_FINA", "NU_PG_FINAL_ID")]
    public void FallbackFieldAliases_MapTruncatedDbfNamesToFullNames(string layer, string truncated, string full)
    {
        Profile().FallbackFieldAliases(layer)[truncated].Should().Be(full);
    }

    [Theory]
    [InlineData("REDE_BT")]
    [InlineData("REDE_MT")]
    [InlineData("REDE_LT")]
    [InlineData("INSTALACAO")]
    [InlineData("POSTE")]
    public void FallbackFieldAliases_ExistForEveryLayer(string layer)
    {
        Profile().FallbackFieldAliases(layer).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("REDE_BT")]
    [InlineData("REDE_MT")]
    [InlineData("REDE_LT")]
    [InlineData("INSTALACAO")]
    [InlineData("POSTE")]
    public void MapperFor_EveryLayerReturnsAMapper(string layer)
    {
        Profile().MapperFor(layer).Should().NotBeNull();
    }
}
