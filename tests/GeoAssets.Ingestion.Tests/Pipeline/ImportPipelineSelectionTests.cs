using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineSelectionTests
{
    private static string[] ScopesOf(PipelineScenario scenario) =>
        [.. scenario.Sink.Features.Select(f => f.Properties.CustomAttributes[FeatureImportAttributes.Scope]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Run_ReadsOnlyUsableLayersByDefault()
    {
        var scenario = new PipelineScenario();
        var usable = scenario.AddLayer("POLES");
        var geometryOnly = scenario.AddLayer("DEVICES", verdict: LayerVerdict.GeometryOnly);
        var unusable = scenario.AddLayer("LINES", verdict: LayerVerdict.Unusable);
        scenario.AddPole(usable, 1, 0, 0);
        scenario.AddPole(geometryOnly, 2, 5, 5, assetClass: "Device");
        scenario.AddPole(unusable, 3, 9, 9);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Source.Reads.Select(r => r.Layer).Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(["POLES"]);
        scenario.Sink.Features.Select(f => f.Id).Should().Equal(TestData.PoleId(1));
        report.ElementsByAssetClass.Should().ContainKey("Pole").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task Run_IncludeGeometryOnly_ReadsGeometryOnlyLayersButNeverUnusableOnes()
    {
        var scenario = new PipelineScenario();
        var usable = scenario.AddLayer("POLES");
        var geometryOnly = scenario.AddLayer("DEVICES", verdict: LayerVerdict.GeometryOnly);
        var unusable = scenario.AddLayer("LINES", verdict: LayerVerdict.Unusable);
        scenario.AddPole(usable, 1, 0, 0);
        scenario.AddPole(geometryOnly, 2, 5, 5, assetClass: "Device", completeness: Completeness.GeometryOnly);
        scenario.AddPole(unusable, 3, 9, 9);

        await scenario.RunAsync(r => r with { IncludeGeometryOnly = true }, CancellationToken.None);

        scenario.Source.Reads.Select(r => r.Layer).Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(["POLES", "DEVICES"]);
        scenario.Sink.Features.Select(f => f.Id).Should().BeEquivalentTo([TestData.PoleId(1), TestData.PoleId(2)]);
    }

    [Fact]
    public async Task Run_LayerNotInTheProfile_IsIgnored()
    {
        var scenario = new PipelineScenario();
        var known = scenario.AddLayer("POLES");
        var unknown = scenario.AddLayer("MYSTERY");
        scenario.AddPole(known, 1, 0, 0);
        scenario.AddPole(unknown, 2, 1, 1);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Source.Reads.Select(r => r.Layer).Should().NotContain("MYSTERY");
        scenario.Sink.Features.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_LayerNameMatchingIsCaseInsensitive()
    {
        var scenario = new PipelineScenario();
        var layer = scenario.AddLayer("poles");
        scenario.AddPole(layer, 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_RegionsFilter_KeepsOnlyListedRegionsCaseInsensitively()
    {
        var scenario = new PipelineScenario();
        var north = scenario.AddLayer("POLES", "North");
        var south = scenario.AddLayer("POLES", "South");
        scenario.AddPole(north, 1, 0, 0);
        scenario.AddPole(south, 2, 50, 50);

        await scenario.RunAsync(r => r with { Regions = ["north"] }, CancellationToken.None);

        scenario.Sink.Features.Select(f => f.Id).Should().Equal(TestData.PoleId(1));
        ScopesOf(scenario).Should().Equal("net/North/POLES");
    }

    [Fact]
    public async Task Run_EmptyRegionsAndLayers_SelectEverything()
    {
        var scenario = new PipelineScenario();
        var north = scenario.AddLayer("POLES", "North");
        var south = scenario.AddLayer("POLES", "South");
        scenario.AddPole(north, 1, 0, 0);
        scenario.AddPole(south, 2, 50, 50);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        ScopesOf(scenario).Should().Equal("net/North/POLES", "net/South/POLES");
    }

    [Fact]
    public async Task Run_LayersFilter_KeepsOnlyListedLayersCaseInsensitively()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        var devices = scenario.AddLayer("DEVICES");
        scenario.AddPole(poles, 1, 0, 0);
        scenario.AddPole(devices, 2, 5, 5, assetClass: "Device");

        await scenario.RunAsync(r => r with { Layers = ["devices"] }, CancellationToken.None);

        scenario.Sink.Features.Select(f => f.Id).Should().Equal(TestData.PoleId(2));
    }

    [Fact]
    public async Task Run_RootRegion_UsesScopeWithoutRegionSegment()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        ScopesOf(scenario).Should().Equal("net/POLES");
    }

    [Fact]
    public async Task Run_ProcessesLayersByProcessingOrderThenRegionThenLayerName()
    {
        var scenario = new PipelineScenario();
        var polesSouth = scenario.AddLayer("POLES", "South");
        var polesNorth = scenario.AddLayer("POLES", "North");
        var devicesSouth = scenario.AddLayer("DEVICES", "South");
        scenario.AddPole(polesSouth, 1, 0, 0);
        scenario.AddPole(polesNorth, 2, 10, 0);
        scenario.AddPole(devicesSouth, 3, 20, 0, assetClass: "Device");

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Select(f => f.Properties.CustomAttributes[FeatureImportAttributes.Scope])
            .Should().Equal("net/South/DEVICES", "net/North/POLES", "net/South/POLES");
    }

    [Fact]
    public async Task Run_EnsuresAssetTypesOnceBeforeTheFirstChunk_AndCompletesLast()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.EnsuredClasses.Should().BeEquivalentTo(TestData.AssetClasses);
        scenario.Sink.Events.First().Should().Be("ensure");
        scenario.Sink.Events.Count(e => string.Equals(e, "ensure", StringComparison.Ordinal)).Should().Be(1);
        scenario.Sink.Events.Last().Should().Be("complete");
    }

    [Fact]
    public async Task Run_CopiesInventoryIssuesIntoTheReport()
    {
        var scenario = new PipelineScenario();
        scenario.InventoryIssues.Add(new ImportIssue(IssueSeverity.Warning, ImportIssueCodes.MissingSidecar, "POLES", "no dbf"));
        scenario.InventoryIssues.Add(new ImportIssue(IssueSeverity.Error, ImportIssueCodes.DuplicateFile, "LINES", "dup"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts.Should().Contain(new KeyValuePair<string, long>(ImportIssueCodes.MissingSidecar, 1));
        report.IssueCounts.Should().Contain(new KeyValuePair<string, long>(ImportIssueCodes.DuplicateFile, 1));
        report.IssueSamples.Select(i => i.Subject).Should().BeEquivalentTo(["POLES", "LINES"]);
        report.ProfileId.Should().Be(TestData.ProfileId);
        report.ScopePrefix.Should().Be(PipelineScenario.ScopePrefix);
    }

    [Fact]
    public async Task Run_NothingSelected_WritesNoChunksAndReportsNonePositionAccuracy()
    {
        var scenario = new PipelineScenario();
        scenario.AddLayer("POLES", verdict: LayerVerdict.Unusable);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ChunksWritten.Should().Be(0);
        report.FeaturesWritten.Should().Be(0);
        report.PositionAccuracy.Should().Be("none");
        scenario.Sink.Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_StampsTheOrganizationOnEveryFeature()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);
        var organization = Guid.Parse("0000000a-0000-0000-0000-0000000000ff");

        await scenario.RunAsync(r => r with { OrganizationId = organization }, CancellationToken.None);

        scenario.Sink.Features.Single().Properties.OrganizationId.Should().Be(organization);
    }

    [Fact]
    public async Task Run_CancelledWhileReading_Throws()
    {
        var scenario = new PipelineScenario();
        var layer = scenario.AddLayer("POLES");
        scenario.AddPole(layer, 1, 0, 0);
        scenario.AddPole(layer, 2, 1, 1);
        using var cts = new CancellationTokenSource();
        scenario.Source.OnRecord = _ => cts.CancelAsync();

        var act = () => scenario.RunAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        scenario.Sink.Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_AlreadyCancelled_Throws()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => scenario.RunAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
