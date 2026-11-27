using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

/// <summary><c>plan --export</c> (XD01-466): the plan written again as FlatGeobuf or GeoParquet, read back with the GDAL plugin's reader.</summary>
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
        result.Stderr.Should().Contain("--export must be flatgeobuf or geoparquet");
        File.Exists(output.File(PipelineCommand.FeaturesFile)).Should().BeFalse();
    }
}
