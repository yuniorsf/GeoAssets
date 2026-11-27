using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

/// <summary>
/// <c>plan --export</c>: the plan written again as FlatGeobuf or GeoParquet, read back with the GDAL plugin's reader
/// (XD01-466), or as a PMTiles archive, read back as an offline map pack (XD01-472).
/// </summary>
public sealed class PlanExportTests
{
    private static Task<CliResult> PlanAsync(CoelbaDelivery delivery, TempDirectory output, params string[] extra) =>
        CliRunner.RunAsync(
            ["plan", "--root", delivery.Root, "--profile", "neoenergia-gse", "--scope-prefix", "coelba", "--out", output.Path, .. extra]);

    private static async Task<List<GeoFeature>> ReadPlanAsync(TempDirectory output)
    {
        var lines = await File.ReadAllLinesAsync(output.File(PipelineCommand.FeaturesFile));
        return [.. lines.Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<GeoFeature>(l, GeoJsonSerializer.Options)!)];
    }

    [Theory]
    [InlineData("flatgeobuf", VectorFileFormat.FlatGeobuf, "features.fgb")]
    [InlineData("GeoParquet", VectorFileFormat.GeoParquet, "features.parquet")]
    public async Task Export_WritesEveryPlannedFeature_WithItsGeometryIdNameAndAttributes(string export, VectorFileFormat format, string fileName)
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, "--export", export);

        result.ExitCode.Should().Be(0, result.Output);
        result.Stdout.Should().Contain($"exported as {format}").And.Contain(fileName).And.Contain("(5 features)");
        var planned = await ReadPlanAsync(output);
        var exported = new OgrVectorFileReader().ReadPage(new VectorFileReadRequest(output.File(fileName), format), CancellationToken.None).Features;
        exported.Select(f => f.Id).Should().BeEquivalentTo(planned.Select(f => f.Id));
        foreach (var expected in planned)
        {
            var actual = exported.Single(f => string.Equals(f.Id, expected.Id, StringComparison.Ordinal));
            actual.Properties.Name.Should().Be(expected.Properties.Name);
            foreach (var (key, value) in expected.Properties.CustomAttributes)
                actual.Properties.CustomAttributes.Should().Contain(key, value);
            actual.Geometry!.NtsGeometry.EqualsExact(expected.Geometry!.NtsGeometry, 1e-9).Should().BeTrue(expected.Id);
        }
    }

    [Fact]
    public async Task WithoutExport_OnlyTheNdjsonPlanIsWritten()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        Directory.EnumerateFiles(output.Path).Select(Path.GetFileName).Should().BeEquivalentTo(PipelineCommand.FeaturesFile, PipelineCommand.ReportFile);
    }

    [Fact]
    public async Task AnUnknownExportFormat_IsAnInputError_BeforeThePipelineRuns()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, "--export", "shapefile");

        result.ExitCode.Should().Be(2);
        result.Stderr.Should().Contain("--export must be flatgeobuf, geoparquet or pmtiles");
        File.Exists(output.File(PipelineCommand.FeaturesFile)).Should().BeFalse();
    }

    [Fact]
    public async Task ExportPMTiles_WritesAnOfflinePack_HoldingEveryPlannedFeature_AtEveryZoom_InItsAssetClassLayer()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, "--export", "PMTiles", "--min-zoom", "10", "--max-zoom", "14");

        result.ExitCode.Should().Be(0, result.Output);
        result.Stdout.Should().Contain("exported as PMTiles (zooms 10-14").And.Contain("features.pmtiles").And.Contain("(5 features)");
        var planned = await ReadPlanAsync(output);
        using var archive = await PMTilesArchive.OpenAsync(output.File("features.pmtiles"), CancellationToken.None);
        archive.Header.MinZoom.Should().Be(10);
        archive.Header.MaxZoom.Should().Be(14);
        archive.VectorLayerIds.Should().NotBeEmpty();
        archive.VectorLayerIds.Where(layer => Guid.TryParse(layer, out _)).Should().BeEmpty("each layer is named after its asset class");

        var storage = new PMTilesPackStorage(output.File("packs"));
        using var pack = await storage.ImportAsync(output.File("features.pmtiles"), CancellationToken.None);
        using var provider = new PMTilesPackAssetProvider(pack);
        var bounds = Bounds(planned);
        for (var zoom = 10; zoom <= 14; zoom++)
        {
            var read = await provider.ReadAsync(bounds, mapZoom: zoom + 1, CancellationToken.None);

            read.Select(f => f.Properties.CustomAttributes["id"]).Should().BeEquivalentTo(planned.Select(f => f.Id), "zoom {0} holds every feature once, with its id", zoom);
            foreach (var expected in planned)
            {
                var actual = read.Single(f => string.Equals(f.Properties.CustomAttributes["id"], expected.Id, StringComparison.Ordinal));
                actual.Properties.Name.Should().Be(expected.Properties.Name);
                foreach (var (key, value) in expected.Properties.CustomAttributes)
                    actual.Properties.CustomAttributes.Should().Contain(key, value);
            }

            var layerOfType = read.GroupBy(f => AssetTypeOf(planned, f), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.Properties.CustomAttributes["mvt:layer"]).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            layerOfType.Values.Should().OnlyContain(layers => layers.Count == 1, "an asset type's features share one layer");
            layerOfType.Values.Select(layers => layers[0]).Should().OnlyHaveUniqueItems("each asset class has its own layer");
        }
    }

    private static string AssetTypeOf(List<GeoFeature> planned, GeoFeature read) =>
        planned.Single(p => string.Equals(p.Id, read.Properties.CustomAttributes["id"], StringComparison.Ordinal)).Properties.AssetTypeId;

    [Fact]
    public async Task ExportPMTiles_DefaultsToZooms0To14()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, "--export", "pmtiles");

        result.ExitCode.Should().Be(0, result.Output);
        result.Stdout.Should().Contain("exported as PMTiles (zooms 0-14");
        using var archive = await PMTilesArchive.OpenAsync(output.File("features.pmtiles"), CancellationToken.None);
        archive.Header.MinZoom.Should().Be(0);
        archive.Header.MaxZoom.Should().Be(14);
    }

    [Theory]
    [InlineData("--export", "pmtiles", "--min-zoom", "23", "--min-zoom must be a zoom from 0 to 22, not '23'")]
    [InlineData("--export", "pmtiles", "--max-zoom", "-1", "--max-zoom must be a zoom from 0 to 22, not '-1'")]
    [InlineData("--export", "pmtiles", "--max-zoom", "twelve", "--max-zoom must be a zoom from 0 to 22, not 'twelve'")]
    [InlineData("--export", "pmtiles", "--min-zoom", "15", "--min-zoom (15) must not be above --max-zoom (14)")]
    [InlineData("--export", "flatgeobuf", "--max-zoom", "12", "--min-zoom and --max-zoom go with --export pmtiles")]
    [InlineData("--min-zoom", "3", "--max-zoom", "12", "--min-zoom and --max-zoom go with --export pmtiles")]
    public async Task AnInvalidZoomRange_IsAnInputError_BeforeThePipelineRuns(string option1, string value1, string option2, string value2, string message)
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, option1, value1, option2, value2);

        result.ExitCode.Should().Be(2, result.Output);
        result.Stderr.Should().Contain(message);
        File.Exists(output.File(PipelineCommand.FeaturesFile)).Should().BeFalse();
    }

    private static BoundingBox Bounds(IEnumerable<GeoFeature> features)
    {
        var coordinates = features.SelectMany(f => f.Geometry!.NtsGeometry.Coordinates).ToList();
        return new BoundingBox(
            coordinates.Min(c => c.X) - 0.001, coordinates.Min(c => c.Y) - 0.001, coordinates.Max(c => c.X) + 0.001, coordinates.Max(c => c.Y) + 0.001);
    }
}
