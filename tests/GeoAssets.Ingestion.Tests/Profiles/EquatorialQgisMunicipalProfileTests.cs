using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Profiles;

public sealed class EquatorialQgisMunicipalProfileTests
{
    private const string ProfileId = "equatorial-qgis-municipal";

    private static ISourceProfile Profile() => BuiltInProfiles.CreateRegistry().Find(ProfileId);

    private static Dictionary<string, LayerDefinition> Layers() => Profile().Layers.ToDictionary(l => l.Name, StringComparer.Ordinal);

    private static bool Matches(LayerDefinition layer, string stem) =>
        Regex.IsMatch(stem, layer.FileStemPattern!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact]
    public void Registry_ListsAndFindsTheEquatorialQgisMunicipalProfile()
    {
        var registry = BuiltInProfiles.CreateRegistry();

        registry.ProfileIds.Should().Contain(ProfileId);
        registry.ProfileIds.Should().Contain("neoenergia-gse");
        registry.Find(ProfileId).Id.Should().Be(ProfileId);
    }

    [Fact]
    public void Layers_AreTheFourCemarLayers()
    {
        Layers().Keys.Should().BeEquivalentTo(["TRECHO_SECUNDARIO", "TRECHO_PRIMARIO", "TRAFOS", "PGS"]);
    }

    [Theory]
    [InlineData("TRECHO_SECUNDARIO", "shape-trecho-secundario", ElementKind.Segment, 10)]
    [InlineData("TRECHO_PRIMARIO", "shape-trecho-primario", ElementKind.Segment, 20)]
    [InlineData("TRAFOS", "shape-trafos", ElementKind.Device, 30)]
    [InlineData("PGS", "shape-pgs", ElementKind.Structure, 40)]
    public void Layers_HaveTheirPlaceholderStemKindAndProcessingOrder(string name, string stem, ElementKind kind, int order)
    {
        var layer = Layers()[name];

        layer.FileStem.Should().Be(stem);
        layer.Kind.Should().Be(kind);
        layer.ProcessingOrder.Should().Be(order);
    }

    [Fact]
    public void Layers_AllUseOneMeterSnapToleranceAndFiveMetersEndpointDistance()
    {
        Profile().Layers.Should().OnlyContain(l => l.SnapToleranceMeters == 1.0 && l.MaxEndpointDistanceMeters == 5.0);
    }

    [Fact]
    public void Layers_OnlyTheSecondarySegmentLayerCarriesTheDrawingOffset()
    {
        var layers = Layers();

        layers["TRECHO_SECUNDARIO"].DrawingOffsetX.Should().Be(1);
        layers["TRECHO_SECUNDARIO"].DrawingOffsetY.Should().Be(1);
        layers.Where(l => !string.Equals(l.Key, "TRECHO_SECUNDARIO", StringComparison.Ordinal))
            .Should().OnlyContain(l => l.Value.DrawingOffsetX == 0 && l.Value.DrawingOffsetY == 0);
    }

    [Fact]
    public void Layers_OnlyPgsIsASnapTarget()
    {
        Profile().Layers.Where(l => l.SnapTarget).Select(l => l.Name).Should().Equal("PGS");
    }

    [Fact]
    public void Layers_OnlyTrafosIsAnAnchorNamespaceNamedTrafo()
    {
        Profile().Layers.Where(l => l.AnchorNamespace is not null).Select(l => (l.Name, l.AnchorNamespace)).Should().Equal(("TRAFOS", "TRAFO"));
    }

    [Fact]
    public void Layers_OnlyTheSecondarySegmentLayerCollectsGroupAnchors()
    {
        Profile().Layers.Where(l => l.CollectsGroupAnchors).Select(l => l.Name).Should().Equal("TRECHO_SECUNDARIO");
    }

    [Fact]
    public void Layers_TrafosIndexesItsInstallationKeyFromIntNum()
    {
        Layers()["TRAFOS"].IndexKeys.Should().Equal(new IndexKeyDefinition("INST", "INT_NUM"));
    }

    [Fact]
    public void Layers_EveryLayerDeclaresAFileStemPattern()
    {
        Profile().Layers.Should().OnlyContain(l => !string.IsNullOrEmpty(l.FileStemPattern));
    }

    [Theory]
    [InlineData("TRECHO_SECUNDARIO", "shape-trecho-secundario")]
    [InlineData("TRECHO_SECUNDARIO", "shape-trecho-secundario-paco-do-lumiar")]
    [InlineData("TRECHO_SECUNDARIO", "SHAPE-TRECHO-SECUNDARIO-SAO-LUIS")]
    [InlineData("TRECHO_PRIMARIO", "shape-trecho-primario")]
    [InlineData("TRECHO_PRIMARIO", "shape-trecho-primario-raposa")]
    [InlineData("TRAFOS", "shape-trafos")]
    [InlineData("TRAFOS", "shape-trafos-paco-do-lumiar")]
    [InlineData("TRAFOS", "shape-instalacao")]
    [InlineData("TRAFOS", "Shape-Instalacao-Sao-Jose-de-Ribamar")]
    [InlineData("PGS", "shape-pgs")]
    [InlineData("PGS", "shapes-pgs")]
    [InlineData("PGS", "shape-pgs-paco-do-lumiar")]
    [InlineData("PGS", "shapes-pgs-sao-luis")]
    public void FileStemPatterns_MatchMunicipalitySuffixedStemsCaseInsensitively(string layer, string stem)
    {
        Matches(Layers()[layer], stem).Should().BeTrue();
    }

    [Theory]
    [InlineData("TRECHO_SECUNDARIO", "shape-trecho-primario")]
    [InlineData("TRECHO_SECUNDARIO", "xshape-trecho-secundario")]
    [InlineData("TRECHO_PRIMARIO", "shape-trecho-primariox")]
    [InlineData("TRECHO_PRIMARIO", "shape-trecho-secundario-paco")]
    [InlineData("TRAFOS", "shape-trafosx")]
    [InlineData("TRAFOS", "shape-pgs-paco")]
    [InlineData("PGS", "shapes-pgsx")]
    [InlineData("PGS", "shape-trafos")]
    public void FileStemPatterns_RejectOtherStems(string layer, string stem)
    {
        Matches(Layers()[layer], stem).Should().BeFalse();
    }

    [Fact]
    public void AssetClasses_AreIdenticalToTheNeoenergiaGseProfiles()
    {
        var gse = BuiltInProfiles.CreateRegistry().Find("neoenergia-gse").AssetClasses;

        Profile().AssetClasses.Should().HaveCount(15);
        Profile().AssetClasses.Should().Equal(gse, (a, b) => AreSame(a, b));
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
    [InlineData("TRECHO_SECUNDARIO")]
    [InlineData("TRECHO_PRIMARIO")]
    [InlineData("TRAFOS")]
    [InlineData("PGS")]
    public void FallbackFieldAliases_AreEmptyBecauseQgisKeepsFullNames(string layer)
    {
        Profile().FallbackFieldAliases(layer).Should().BeEmpty();
    }

    [Theory]
    [InlineData("TRECHO_SECUNDARIO")]
    [InlineData("TRECHO_PRIMARIO")]
    [InlineData("TRAFOS")]
    [InlineData("PGS")]
    public void MapperFor_EveryLayerReturnsAMapper(string layer)
    {
        Profile().MapperFor(layer).Should().NotBeNull();
    }

    private static bool AreSame(AssetClassDefinition a, AssetClassDefinition b) =>
        string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.Role, b.Role, StringComparison.Ordinal)
        && a.TypeId == b.TypeId
        && string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
        && a.Geometry == b.Geometry
        && string.Equals(a.Color, b.Color, StringComparison.Ordinal)
        && a.ReuseRoles.SequenceEqual(b.ReuseRoles, StringComparer.Ordinal);
}
