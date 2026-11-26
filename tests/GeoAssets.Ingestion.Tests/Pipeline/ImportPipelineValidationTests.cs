using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineValidationTests
{
    private static ElementReference PoleRef(ReferenceRole role, long id, int? node = null) =>
        TestData.Ref(role, TestData.PoleNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture), node);

    [Fact]
    public async Task Run_KeyResolvedEndpointFartherThanTheLimit_WarnsButStillImports()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((10, 0), (50, 0)), [PoleRef(ReferenceRole.Start, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.EndpointDistance].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.EndpointDistance, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Warning);
        scenario.Sink.Features.Select(f => f.Id).Should().Contain(PipelineScenario.SegmentId(1));
    }

    [Fact]
    public async Task Run_KeyResolvedEndpointWithinTheLimit_RaisesNoIssue()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((4, 0), (50, 0)), [PoleRef(ReferenceRole.Start, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.EndpointDistance);
    }

    [Fact]
    public async Task Run_EndpointLimitComesFromTheDeclaringLayer()
    {
        var scenario = new PipelineScenario();
        scenario.Layers[0] = scenario.Layers[0] with { MaxEndpointDistanceMeters = 20.0 };
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((10, 0), (50, 0)), [PoleRef(ReferenceRole.Start, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.EndpointDistance);
    }

    [Fact]
    public async Task Run_EndReferenceIsMeasuredAgainstTheLastVertex()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0)), [PoleRef(ReferenceRole.End, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.EndpointDistance].Should().Be(1);
    }

    [Fact]
    public async Task Run_StartAndEndOnTheSameTargetAndNode_ReportsASelfLoopButImports()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (0, 1)),
            [PoleRef(ReferenceRole.Start, 100, 1), PoleRef(ReferenceRole.End, 100, 1)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.SelfLoop].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.SelfLoop, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Info);
        scenario.Sink.Features.Select(f => f.Id).Should().Contain(PipelineScenario.SegmentId(1));
    }

    [Fact]
    public async Task Run_StartAndEndOnTheSameTargetButDifferentNodes_IsNotASelfLoop()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (0, 1)),
            [PoleRef(ReferenceRole.Start, 100, 1), PoleRef(ReferenceRole.End, 100, 2)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.SelfLoop);
    }

    [Fact]
    public async Task Run_LineStringClassWithAPointGeometry_IsAGeometryMismatchAndSkipped()
    {
        var scenario = new PipelineScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Point(1, 1), []);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.GeometryMismatch].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.GeometryMismatch, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Error);
        report.ElementsByAssetClass.Should().NotContainKey("Segment");
    }

    [Fact]
    public async Task Run_PointClassWithALineGeometry_IsAGeometryMismatchAndSkipped()
    {
        var scenario = new PipelineScenario();
        scenario.AddElement(scenario.AddLayer("POLES"),
            scope => TestData.Element(TestData.PoleKey(1), ElementKind.Structure, "Pole", TestData.Line((0, 0), (1, 1)), scope),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = 1L });

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.GeometryMismatch].Should().Be(1);
    }

    [Fact]
    public async Task Run_SinglePartMultiLineStringForALineClass_IsUnwrapped()
    {
        var scenario = new PipelineScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.MultiLine(TestData.Line((0, 0), (5, 5))), []);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var feature = scenario.Sink.Features.Single();
        feature.Geometry!.GeometryType.Should().Be(GeometryType.LineString);
        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.GeometryMismatch);
    }

    [Fact]
    public async Task Run_SinglePartMultiPointForAPointClass_IsUnwrapped()
    {
        var scenario = new PipelineScenario();
        scenario.AddElement(scenario.AddLayer("POLES"),
            scope => TestData.Element(TestData.PoleKey(1), ElementKind.Structure, "Pole", TestData.MultiPoint(TestData.Point(2, 3)), scope),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = 1L });

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Single().Geometry!.GeometryType.Should().Be(GeometryType.Point);
    }

    [Fact]
    public async Task Run_MultiPartMultiLineString_IsAGeometryMismatch()
    {
        var scenario = new PipelineScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1,
            TestData.MultiLine(TestData.Line((0, 0), (1, 1)), TestData.Line((5, 5), (6, 6))), []);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.GeometryMismatch].Should().Be(1);
    }

    [Fact]
    public async Task Run_UnknownAssetClass_IsAnUnmappedRecordAndSkipped()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 1, 0, 0, assetClass: "Spaceship");

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.UnmappedRecord].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.UnmappedRecord, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public async Task Run_MapperSkippingARecord_RecordsThatIssueAndContinues()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 1, 0, 0);
        scenario.AddPole(poles, 2, 1, 1);
        scenario.Mapper.Script("", "POLES", 2, _ => MappingResult.Skipped(
            new ImportIssue(IssueSeverity.Warning, ImportIssueCodes.UnmappedRecord, "POLES#2", "unknown type")));
        scenario.AddPole(poles, 3, 3, 3);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Select(f => f.Id).Should().Equal(TestData.PoleId(1), TestData.PoleId(3));
        report.IssueCounts[ImportIssueCodes.UnmappedRecord].Should().Be(1);
    }

    [Fact]
    public async Task Run_LayerWithoutACrs_IsUnsupportedAndSkipped()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES", crsWkt: null), 1, 0, 0);
        scenario.AddPole(scenario.AddLayer("DEVICES"), 2, 5, 5, assetClass: "Device");

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Select(f => f.Id).Should().Equal(TestData.PoleId(2));
        report.IssueCounts[ImportIssueCodes.UnsupportedCrs].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.UnsupportedCrs, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public async Task Run_FactoryRejectingTheCrs_IsUnsupportedAndTheLayerSkipped()
    {
        var scenario = new PipelineScenario();
        scenario.Transformers.Rejecting("CRS-BAD");
        scenario.AddPole(scenario.AddLayer("POLES", crsWkt: "CRS-BAD"), 1, 0, 0);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.UnsupportedCrs].Should().Be(1);
    }

    [Fact]
    public async Task Run_OneTransformerPerDistinctCrs()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES", "A", crsWkt: "CRS-A"), 1, 0, 0);
        scenario.AddPole(scenario.AddLayer("POLES", "B", crsWkt: "CRS-A"), 2, 1, 1);
        scenario.AddPole(scenario.AddLayer("DEVICES", "A", crsWkt: "CRS-B"), 3, 2, 2, assetClass: "Device");

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Transformers.Created.Should().BeEquivalentTo(["CRS-A", "CRS-B"]);
    }

    [Fact]
    public async Task Run_PositionAccuracy_IsHelmertWhenAnyUsedTransformerIsHelmert()
    {
        var scenario = new PipelineScenario();
        scenario.Transformers.WithAccuracy("CRS-GRID", "grid").WithAccuracy("CRS-HELM", "helmert");
        scenario.AddPole(scenario.AddLayer("DEVICES", crsWkt: "CRS-GRID"), 1, 0, 0, assetClass: "Device");
        scenario.AddPole(scenario.AddLayer("POLES", crsWkt: "CRS-HELM"), 2, 1, 1);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.PositionAccuracy.Should().Be("helmert");
    }

    [Fact]
    public async Task Run_PositionAccuracy_IsTheFirstTransformersWhenNoneIsHelmert()
    {
        var scenario = new PipelineScenario();
        scenario.Transformers.WithAccuracy("CRS-GRID", "grid").WithAccuracy("CRS-NONE", "none");
        scenario.AddPole(scenario.AddLayer("DEVICES", crsWkt: "CRS-GRID"), 1, 0, 0, assetClass: "Device");
        scenario.AddPole(scenario.AddLayer("POLES", crsWkt: "CRS-NONE"), 2, 1, 1);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.PositionAccuracy.Should().Be("grid");
    }

    [Fact]
    public async Task Run_StampsEachFeaturesPositionAccuracyFromItsLayersTransformer()
    {
        var scenario = new PipelineScenario();
        scenario.Transformers.WithAccuracy("CRS-GRID", "grid");
        scenario.AddPole(scenario.AddLayer("POLES", crsWkt: "CRS-GRID"), 1, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Single().Properties.CustomAttributes[FeatureImportAttributes.PositionAccuracy].Should().Be("grid");
    }

    [Fact]
    public async Task Run_ManyIssuesOfOneCode_CountsAllButKeepsAtMostTwentySamples()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        var lines = scenario.AddLayer("LINES");
        for (var i = 1; i <= 25; i++)
        {
            scenario.AddSegment(lines, i, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.Start, 9000 + i)]);
        }

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(25);
        report.IssueSamples.Count(i => string.Equals(i.Code, ImportIssueCodes.DanglingReference, StringComparison.Ordinal)).Should().Be(20);
        report.UnresolvedReferences.Should().Be(25);
    }
}
