using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class PlanCommandTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Task<CliResult> PlanAsync(CoelbaDelivery delivery, TempDirectory output, params string[] extra) =>
        CliRunner.RunAsync(
            ["plan", "--root", delivery.Root, "--profile", "neoenergia-gse", "--scope-prefix", "coelba", "--out", output.Path, .. extra]);

    private static async Task<List<GeoFeature>> ReadFeaturesAsync(TempDirectory output)
    {
        var lines = await File.ReadAllLinesAsync(output.File("features.ndjson"));
        return [.. lines.Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<GeoFeature>(l, GeoJsonSerializer.Options)!)];
    }

    private static async Task<ImportReport> ReadReportAsync(TempDirectory output) =>
        JsonSerializer.Deserialize<ImportReport>(await File.ReadAllTextAsync(output.File("report.json")), Web)!;

    [Fact]
    public async Task Plan_WritesOneGeoFeaturePerLineAndAReport_ExitingZero()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output);

        result.ExitCode.Should().Be(0, result.Output);
        var features = await ReadFeaturesAsync(output);
        features.Should().HaveCount(5);
        features.Select(f => f.Id).Should().OnlyHaveUniqueItems();
        features.Should().OnlyContain(f => f.Geometry != null);
    }

    [Fact]
    public async Task Plan_NdjsonIsOneFeaturePerLineWithoutIndentation()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        var lines = (await File.ReadAllLinesAsync(output.File("features.ndjson"))).Where(l => l.Length > 0).ToArray();
        lines.Should().HaveCount(5);
        lines.Should().OnlyContain(l => l.StartsWith('{') && l.EndsWith('}'));
    }

    [Fact]
    public async Task Plan_FeaturesCarryProvenanceScopeAndTheClassTypeId()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        var features = await ReadFeaturesAsync(output);
        var scopes = features.Select(f => f.Properties.CustomAttributes[FeatureImportAttributes.Scope]).Distinct(StringComparer.Ordinal);
        scopes.Should().BeEquivalentTo(["coelba/POSTE", "coelba/INSTALACAO", "coelba/REDE_MT"]);
        features.Should().OnlyContain(f => string.Equals(f.Properties.CustomAttributes[FeatureImportAttributes.Profile], "neoenergia-gse", StringComparison.Ordinal));
        var pole = features.First(f => string.Equals(f.Properties.Name, "Poste 1", StringComparison.Ordinal));
        pole.Properties.AssetTypeId.Should().Be("5b3d5dde-182f-4725-bd56-e35846c77324");
        pole.Properties.Srid.Should().Be(4326);
        var transformer = features.Single(f => string.Equals(f.Properties.Name, "DJ 10", StringComparison.Ordinal));
        transformer.Properties.AssetTypeId.Should().Be("61628e59-1dc2-4015-962d-f8350111aa12");
    }

    [Fact]
    public async Task Plan_ReprojectsToWgs84NearSalvador()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        var pole = (await ReadFeaturesAsync(output)).First(f => string.Equals(f.Properties.Name, "Poste 1", StringComparison.Ordinal));
        var point = (NetTopologySuite.Geometries.Point)pole.Geometry!.NtsGeometry;
        point.X.Should().BeInRange(-38.6, -38.4);
        point.Y.Should().BeInRange(-13.1, -12.9);
    }

    [Fact]
    public async Task Plan_ResolvesReferencesAsTopologyEdges()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        var features = await ReadFeaturesAsync(output);
        var line = features.Single(f => string.Equals(f.Properties.Name, "MT 100", StringComparison.Ordinal));
        line.Topology.Should().HaveCount(2);
        line.Topology.Should().OnlyContain(e => string.Equals(e.Kind, "connected-to", StringComparison.Ordinal));
        var device = features.Single(f => string.Equals(f.Properties.Name, "DJ 10", StringComparison.Ordinal));
        device.Topology.Should().ContainSingle(e => string.Equals(e.Kind, "mounted-on", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_ReportCountsFeaturesPerClassAndNamesThePositionAccuracy()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output);

        var report = await ReadReportAsync(output);
        report.ProfileId.Should().Be("neoenergia-gse");
        report.ScopePrefix.Should().Be("coelba");
        report.FeaturesWritten.Should().Be(5);
        report.ElementsByAssetClass["Pole"].Should().Be(2);
        report.ElementsByAssetClass["GeographicPoint"].Should().Be(1);
        report.ElementsByAssetClass["Breaker"].Should().Be(1);
        report.ElementsByAssetClass["MvSegment"].Should().Be(1);
        report.UnresolvedReferences.Should().Be(0);
        report.PositionAccuracy.Should().BeOneOf("grid", "helmert");
    }

    [Fact]
    public async Task Plan_LayersOption_RestrictsTheImportedLayers()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await PlanAsync(delivery, output, "--layers", "POSTE");

        (await ReadFeaturesAsync(output)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Plan_RegionsOption_RestrictsTheImportedRegions()
    {
        using var delivery = new CoelbaDelivery();
        delivery.AddRegion("Norte", 2);
        using var output = new TempDirectory();

        await PlanAsync(delivery, output, "--regions", "Norte");

        var features = await ReadFeaturesAsync(output);
        features.Should().HaveCount(2);
        features.Should().OnlyContain(f => string.Equals(f.Properties.CustomAttributes[FeatureImportAttributes.Scope], "coelba/Norte/POSTE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_GeometryOnlyLayers_AreSkippedUnlessRequested()
    {
        using var delivery = new CoelbaDelivery();
        delivery.AddGeometryOnlyLowVoltage();
        using var withoutFlag = new TempDirectory();
        using var withFlag = new TempDirectory();

        await PlanAsync(delivery, withoutFlag);
        await PlanAsync(delivery, withFlag, "--include-geometry-only");

        (await ReadFeaturesAsync(withoutFlag)).Should().HaveCount(5);
        var included = await ReadFeaturesAsync(withFlag);
        included.Should().HaveCount(6);
        included.Single(f => string.Equals(f.Properties.CustomAttributes[FeatureImportAttributes.Scope], "coelba/REDE_BT", StringComparison.Ordinal))
            .Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().NotBe("full");
    }

    [Fact]
    public async Task Plan_ProjDataOption_IsAccepted()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await PlanAsync(delivery, output, "--proj-data", "/opt/homebrew/share/proj");

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public async Task Plan_NeverTouchesTheKernel()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler();

        await CliRunner.RunAsync(
            handler, "plan", "--root", delivery.Root, "--profile", "neoenergia-gse", "--scope-prefix", "coelba", "--out", output.Path);

        handler.Requests.Should().BeEmpty();
    }
}
