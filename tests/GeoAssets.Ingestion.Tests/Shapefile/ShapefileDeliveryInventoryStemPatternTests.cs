using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Shapefile;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Shapefile;

public sealed class ShapefileDeliveryInventoryStemPatternTests : IDisposable
{
    private const string Region = "Paco do Lumiar";

    private readonly ShapefileDeliveryBuilder _delivery = new();

    public void Dispose() => _delivery.Dispose();

    private static FakeSourceProfile CemarLikeProfile() => new(
        [
            new LayerDefinition
            {
                Name = "TRAFOS",
                FileStem = "shape-trafos",
                FileStemPattern = "^shape-(trafos|instalacao)(-.+)?$",
                Kind = ElementKind.Device,
            },
            new LayerDefinition
            {
                Name = "PGS",
                FileStem = "shape-pgs",
                FileStemPattern = "^shapes?-pgs(-.+)?$",
                Kind = ElementKind.Structure,
            },
            new LayerDefinition { Name = "PLAIN", FileStem = "PLAIN", Kind = ElementKind.Structure },
        ],
        TestData.AssetClasses,
        new ScriptedLayerMapper());

    private Task<DeliveryInventory> InspectAsync(FakeSourceProfile? profile = null) =>
        new ShapefileDeliveryInventory().InspectAsync(_delivery.Root, profile ?? CemarLikeProfile(), CancellationToken.None);

    private static IEnumerable<LayerFileSet> Of(DeliveryInventory inventory, string layer) =>
        inventory.Layers.Where(l => string.Equals(l.Layer, layer, StringComparison.Ordinal));

    [Theory]
    [InlineData("shape-pgs")]
    [InlineData("shape-pgs-paco-do-lumiar")]
    [InlineData("shapes-pgs-sao-jose-de-ribamar")]
    [InlineData("SHAPE-PGS-PACO")]
    [InlineData("Shapes-Pgs")]
    public async Task Inspect_StemMatchingThePattern_BecomesThatLayer(string stem)
    {
        _delivery.WriteLayer(Region, stem, ShapefileTestFeatures.Poles(3));

        var layer = Of(await InspectAsync(), "PGS").Single();

        layer.Region.Should().Be(Region);
        layer.Verdict.Should().Be(LayerVerdict.Usable);
        layer.RecordCount.Should().Be(3);
        layer.GeometryPath.Should().Be(_delivery.PathOf(Region, stem + ".shp"));
        layer.AttributeTablePath.Should().Be(_delivery.PathOf(Region, stem + ".dbf"));
    }

    [Theory]
    [InlineData("shape-trafos-paco")]
    [InlineData("shape-instalacao")]
    [InlineData("SHAPE-INSTALACAO-Sao-Luis")]
    public async Task Inspect_AlternationInThePattern_MatchesEitherName(string stem)
    {
        _delivery.WriteLayer(Region, stem, ShapefileTestFeatures.Poles(2));

        Of(await InspectAsync(), "TRAFOS").Should().ContainSingle();
    }

    [Theory]
    [InlineData("xshape-pgs")]
    [InlineData("shape-pgsx")]
    [InlineData("shape-pgs-")]
    [InlineData("shape-trechos-primario")]
    [InlineData("pgs")]
    public async Task Inspect_StemNotFullyMatchingThePattern_IsIgnored(string stem)
    {
        _delivery.WriteLayer(Region, stem, ShapefileTestFeatures.Poles(2));

        var inventory = await InspectAsync();

        inventory.Layers.Should().BeEmpty();
        inventory.Issues.Should().NotContain(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_WithAPattern_TheLiteralFileStemNoLongerDecidesTheMatch()
    {
        var profile = new FakeSourceProfile(
            [new LayerDefinition { Name = "ONLY", FileStem = "placeholder", FileStemPattern = "^real-.+$", Kind = ElementKind.Structure }],
            TestData.AssetClasses,
            new ScriptedLayerMapper());
        _delivery.WriteLayer(Region, "placeholder", ShapefileTestFeatures.Poles(2));
        _delivery.WriteLayer(Region, "real-one", ShapefileTestFeatures.Poles(2, 600000));

        var layer = (await InspectAsync(profile)).Layers.Single();

        layer.GeometryPath.Should().Be(_delivery.PathOf(Region, "real-one.shp"));
    }

    [Fact]
    public async Task Inspect_LayersWithoutAPattern_StillMatchTheirFileStemExactly()
    {
        _delivery.WriteLayer(Region, "plain", ShapefileTestFeatures.Poles(2));
        _delivery.WriteLayer(Region, "PLAIN-extra", ShapefileTestFeatures.Poles(2, 600000));

        var layers = (await InspectAsync()).Layers;

        layers.Should().ContainSingle().Which.Layer.Should().Be("PLAIN");
    }

    [Fact]
    public async Task Inspect_EachRegionMatchesItsOwnMunicipalitySuffixedStems()
    {
        _delivery.WriteLayer("Paco", "shape-pgs-paco", ShapefileTestFeatures.Poles(2));
        _delivery.WriteLayer("Sao Luis", "shape-pgs-sao-luis", ShapefileTestFeatures.Poles(3, 600000));

        var inventory = await InspectAsync();

        var layers = Of(inventory, "PGS").ToArray();
        layers.Select(l => l.Region).Should().BeEquivalentTo(["Paco", "Sao Luis"]);
        layers.Should().OnlyContain(l => l.Verdict == LayerVerdict.Usable);
        inventory.Issues.Should().NotContain(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_TwoStemsMatchingOneLayerInARegion_UsesTheFirstOrdinalAndWarnsAboutTheOther()
    {
        _delivery.WriteLayer(Region, "shape-pgs-b", ShapefileTestFeatures.Poles(2, 600000));
        _delivery.WriteLayer(Region, "shape-pgs-a", ShapefileTestFeatures.Poles(3));

        var inventory = await InspectAsync();

        var layer = Of(inventory, "PGS").Single();
        layer.GeometryPath.Should().Be(_delivery.PathOf(Region, "shape-pgs-a.shp"));
        layer.RecordCount.Should().Be(3);
        var duplicates = inventory.Issues.Where(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal)).ToArray();
        duplicates.Should().ContainSingle();
        duplicates[0].Severity.Should().Be(IssueSeverity.Warning);
        duplicates[0].Subject.Should().Contain("shape-pgs-b");
    }

    [Fact]
    public async Task Inspect_ThreeStemsMatchingOneLayer_WarnsOncePerIgnoredStem()
    {
        _delivery.WriteLayer(Region, "shape-pgs-a", ShapefileTestFeatures.Poles(3));
        _delivery.WriteLayer(Region, "shape-pgs-b", ShapefileTestFeatures.Poles(2, 600000));
        _delivery.WriteLayer(Region, "shape-pgs-c", ShapefileTestFeatures.Poles(2, 700000));

        var inventory = await InspectAsync();

        Of(inventory, "PGS").Should().ContainSingle();
        inventory.Issues.Count(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task Inspect_StemsMatchingDifferentLayersInOneRegion_AreNotDuplicates()
    {
        _delivery.WriteLayer(Region, "shape-pgs-paco", ShapefileTestFeatures.Poles(3));
        _delivery.WriteLayer(Region, "shape-trafos-paco", ShapefileTestFeatures.Poles(2, 600000));

        var inventory = await InspectAsync();

        inventory.Layers.Select(l => l.Layer).Should().BeEquivalentTo(["PGS", "TRAFOS"]);
        inventory.Issues.Should().NotContain(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_PatternMatchedStemWithoutGeometry_IsNotAnOrphanCandidateForAnotherLayer()
    {
        _delivery.WriteLayer(Region, "shape-trafos-paco", ShapefileTestFeatures.Poles(3), dbf: false);
        _delivery.WriteOrphanTable(Region, "shape-pgs-paco", ShapefileTestFeatures.Poles(3));

        var inventory = await InspectAsync();

        var trafos = Of(inventory, "TRAFOS").Single();
        trafos.Verdict.Should().Be(LayerVerdict.GeometryOnly);
        trafos.AttributeTablePath.Should().BeNull();
        inventory.Issues.Should().NotContain(i => string.Equals(i.Code, ImportIssueCodes.OrphanAttributeTable, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_OrphanDbfNamedHWithTheSameRecordCount_StillPairs()
    {
        _delivery.WriteLayer(Region, "shape-trafos-sjr", ShapefileTestFeatures.Poles(4), dbf: false);
        var orphan = _delivery.WriteOrphanTable(Region, "h", ShapefileTestFeatures.Poles(4));

        var inventory = await InspectAsync();

        var layer = Of(inventory, "TRAFOS").Single();
        layer.Verdict.Should().Be(LayerVerdict.Usable);
        layer.AttributeTablePath.Should().Be(orphan);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.OrphanAttributeTable, StringComparison.Ordinal) && i.Severity == IssueSeverity.Info);
    }

    [Fact]
    public async Task Inspect_OrphanDbfNamedHWithADifferentRecordCount_IsNotPaired()
    {
        _delivery.WriteLayer(Region, "shape-trafos-sjr", ShapefileTestFeatures.Poles(4), dbf: false);
        _delivery.WriteOrphanTable(Region, "h", ShapefileTestFeatures.Poles(6));

        var layer = Of(await InspectAsync(), "TRAFOS").Single();

        layer.Verdict.Should().Be(LayerVerdict.GeometryOnly);
        layer.AttributeTablePath.Should().BeNull();
    }
}
