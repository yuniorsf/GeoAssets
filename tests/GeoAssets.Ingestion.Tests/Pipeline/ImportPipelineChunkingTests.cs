using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineChunkingTests
{
    private static string ScopeOf(IEnumerable<GeoFeature> features) =>
        features.Select(f => f.Properties.CustomAttributes[FeatureImportAttributes.Scope]).Distinct(StringComparer.Ordinal).Single();

    [Fact]
    public async Task Run_FlushesWhenTheChunkSizeIsReached()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        for (var i = 1; i <= 5; i++)
        {
            scenario.AddPole(poles, i, i, i);
        }

        var report = await scenario.RunAsync(r => r with { ChunkSize = 2 }, CancellationToken.None);

        scenario.Sink.Chunks.Select(c => c.Features.Count).Should().Equal(2, 2, 1);
        report.ChunksWritten.Should().Be(3);
        report.FeaturesWritten.Should().Be(5);
    }

    [Fact]
    public async Task Run_FlushesWhenTheScopeChanges_SoEveryChunkHoldsOneScope()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES", "North"), 1, 0, 0);
        scenario.AddPole(scenario.AddLayer("POLES", "South"), 2, 9, 9);

        await scenario.RunAsync(r => r with { ChunkSize = 100 }, CancellationToken.None);

        scenario.Sink.Chunks.Should().HaveCount(2);
        scenario.Sink.Chunks.Select(c => ScopeOf(c.Features)).Should().Equal("net/North/POLES", "net/South/POLES");
    }

    [Fact]
    public async Task Run_SequenceNumbersIncreaseByOneAcrossChunks()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        for (var i = 1; i <= 4; i++)
        {
            scenario.AddPole(poles, i, i, i);
        }

        await scenario.RunAsync(r => r with { ChunkSize = 1 }, CancellationToken.None);

        var sequences = scenario.Sink.Chunks.Select(c => c.Sequence).ToArray();
        sequences.Should().HaveCount(4);
        sequences.Zip(sequences.Skip(1), (a, b) => b - a).Should().OnlyContain(step => step == 1);
        sequences.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Run_SinkRejections_BecomeIssuesAndDoNotCountAsWritten()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 1, 0, 0);
        scenario.AddPole(poles, 2, 1, 1);
        scenario.Sink.Rejector = (_, features) =>
            [new ImportIssue(IssueSeverity.Error, ImportIssueCodes.Rejected, features[0].Id, "foreign-organization")];

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.FeaturesWritten.Should().Be(1);
        report.IssueCounts[ImportIssueCodes.Rejected].Should().Be(1);
        report.IssueSamples.Should().ContainSingle(i => string.Equals(i.Code, ImportIssueCodes.Rejected, StringComparison.Ordinal) && i.Message == "foreign-organization");
    }

    [Fact]
    public async Task Run_CompletesTheSinkAfterTheLastChunk()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Events.Should().HaveCount(3);
        scenario.Sink.Events[1].Should().StartWith("write:");
        scenario.Sink.Events[2].Should().Be("complete");
    }

    [Fact]
    public async Task Run_DefaultChunkSize_IsTheKernelsMaximum()
    {
        var scenario = new PipelineScenario();
        scenario.Request().ChunkSize.Should().Be(FeatureImportLimits.MaxChunkSize);
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Chunks.Should().ContainSingle();
    }
}
