using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineGroupAnchorTests
{
    private const string AnchorNamespace = "TRAFO";

    private static PipelineScenario NewScenario(double lineTolerance = 1.0)
    {
        var scenario = new PipelineScenario();
        scenario.Layers[0] = scenario.Layers[0] with { CollectsGroupAnchors = true, SnapToleranceMeters = lineTolerance };
        scenario.Layers[1] = scenario.Layers[1] with { AnchorNamespace = AnchorNamespace };
        return scenario;
    }

    private static GeoFeature Feature(PipelineScenario scenario, string id) =>
        scenario.Sink.Features.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    private static string DeviceId(long id) =>
        DeterministicFeatureId.For(new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static void AddDevice(PipelineScenario scenario, LayerFileSet layer, long id, double x, double y) =>
        scenario.AddElement(
            layer,
            scope => TestData.Element(
                new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ElementKind.Device,
                "Device",
                TestData.Point(x, y),
                scope));

    private static ElementReference GroupRef(string groupKey, string targetNamespace = AnchorNamespace) =>
        new(ReferenceRole.FedBy, targetNamespace, null, GroupKey: groupKey);

    private static ElementReference[] Group(string groupKey) => [GroupRef(groupKey)];

    private static ImportIssue Single(ImportReport report, string code) =>
        report.IssueSamples.Single(i => string.Equals(i.Code, code, StringComparison.Ordinal));

    [Fact]
    public async Task Run_UniqueAnchorWithinTolerance_ProducesFeedsEdgesFromTheAnchorToEveryGroupElement()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0.3, 0.4), (100, 0)), Group("g"));
        scenario.AddSegment(lines, 2, TestData.Line((50, 0), (0.6, 0.8)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edges = Feature(scenario, DeviceId(7)).Topology;
        edges.Should().HaveCount(2);
        edges.Should().OnlyContain(e => e.Kind == "feeds");
        edges.Select(e => e.TargetId).Should().BeEquivalentTo([PipelineScenario.SegmentId(1), PipelineScenario.SegmentId(2)]);
        edges.Should().OnlyContain(e => e.Metadata["resolution"] == "anchor" && e.Metadata["distance"] == "0.50");
        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().BeEmpty();
        Feature(scenario, PipelineScenario.SegmentId(2)).Topology.Should().BeEmpty();
        report.ReferencesByMethod["anchor"].Should().Be(2);
        report.UnresolvedReferences.Should().Be(0);
    }

    [Fact]
    public async Task Run_NoCandidateWithinTolerance_IsADanglingReferenceRecordedOncePerGroup()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 50, 50);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0, 0), (10, 0)), Group("g"));
        scenario.AddSegment(lines, 2, TestData.Line((10, 0), (20, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
        Single(report, ImportIssueCodes.DanglingReference).Severity.Should().Be(IssueSeverity.Warning);
        report.UnresolvedReferences.Should().Be(2);
        report.ReferencesByMethod["anchor"].Should().Be(0);
    }

    [Fact]
    public async Task Run_TwoCandidates_IsOneAmbiguousAnchorIssuePerGroupAndEveryElementStaysUnresolved()
    {
        var scenario = NewScenario();
        var devices = scenario.AddLayer("DEVICES");
        AddDevice(scenario, devices, 7, 0, 0);
        AddDevice(scenario, devices, 8, 0.5, 0);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0.2, 0), (10, 0)), Group("g"));
        scenario.AddSegment(lines, 2, TestData.Line((20, 0), (0.3, 0)), Group("g"));
        scenario.AddSegment(lines, 3, TestData.Line((30, 0), (40, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.AmbiguousAnchor].Should().Be(1);
        var issue = Single(report, ImportIssueCodes.AmbiguousAnchor);
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.Message.Should().Contain("2");
        report.UnresolvedReferences.Should().Be(3);
        report.ReferencesByMethod["anchor"].Should().Be(0);
        Feature(scenario, DeviceId(7)).Topology.Should().BeEmpty();
        Feature(scenario, DeviceId(8)).Topology.Should().BeEmpty();
        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.DanglingReference);
    }

    [Fact]
    public async Task Run_OneCandidateNearSeveralNodes_IsStillUniqueWithTheSmallestDistance()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0.9, 0), (100, 0)), Group("g"));
        scenario.AddSegment(lines, 2, TestData.Line((200, 0), (0.25, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Should().OnlyContain(e => e.Metadata["distance"] == "0.25");
        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.AmbiguousAnchor);
    }

    [Fact]
    public async Task Run_ToleranceIsTheDeclaringLayers()
    {
        var wide = NewScenario(lineTolerance: 3.0);
        AddDevice(wide, wide.AddLayer("DEVICES"), 7, 0, 0);
        wide.AddSegment(wide.AddLayer("LINES"), 1, TestData.Line((2.5, 0), (100, 0)), Group("g"));
        var narrow = NewScenario();
        AddDevice(narrow, narrow.AddLayer("DEVICES"), 7, 0, 0);
        narrow.AddSegment(narrow.AddLayer("LINES"), 1, TestData.Line((2.5, 0), (100, 0)), Group("g"));

        var wideReport = await wide.RunAsync(cancellationToken: CancellationToken.None);
        var narrowReport = await narrow.RunAsync(cancellationToken: CancellationToken.None);

        wideReport.ReferencesByMethod["anchor"].Should().Be(1);
        Feature(wide, DeviceId(7)).Topology.Single().Metadata["distance"].Should().Be("2.50");
        narrowReport.ReferencesByMethod["anchor"].Should().Be(0);
        narrowReport.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
    }

    [Fact]
    public async Task Run_NodesComeFromTheStartAndEndSnapPointsWhenTheElementDeclaresThem()
    {
        var scenario = NewScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 500, 500);
        scenario.AddPole(poles, 101, 600, 600);
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 500.5, 500);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((500, 500), (600, 600)),
        [
            TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(500, 500)),
            TestData.Ref(ReferenceRole.End, TestData.PoleNamespace, null, snap: TestData.Point(600, 600)),
            GroupRef("g"),
        ]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, DeviceId(7)).Topology.Single();
        edge.Kind.Should().Be("feeds");
        edge.TargetId.Should().Be(PipelineScenario.SegmentId(1));
        edge.Metadata["distance"].Should().Be("0.50");
        report.ReferencesByMethod["anchor"].Should().Be(1);
        report.ReferencesByMethod["snap"].Should().Be(2);
        report.UnresolvedReferences.Should().Be(0);
    }

    [Fact]
    public async Task Run_WhenStartAndEndDeclareSnapPoints_TheGeometryEndpointsAreNotNodes()
    {
        var scenario = NewScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 500, 500);
        scenario.AddPole(poles, 101, 600, 600);
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.1, 0), (0.2, 0)),
        [
            TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(500, 500)),
            TestData.Ref(ReferenceRole.End, TestData.PoleNamespace, null, snap: TestData.Point(600, 600)),
            GroupRef("g"),
        ]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Should().BeEmpty();
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
        report.UnresolvedReferences.Should().Be(1);
    }

    [Fact]
    public async Task Run_WithoutSnapPoints_TheGeometryFirstAndLastCoordinatesAreTheNodes()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 100.4, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0), (100, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Single().Metadata["distance"].Should().Be("0.40");
        report.ReferencesByMethod["anchor"].Should().Be(1);
    }

    [Fact]
    public async Task Run_InteriorVerticesOfTheGeometryAreNotNodes()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 50, 0.2);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0), (100, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ReferencesByMethod["anchor"].Should().Be(0);
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
    }

    [Fact]
    public async Task Run_OnlyLayersWithTheMatchingAnchorNamespaceProvideCandidates()
    {
        var scenario = NewScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.Layers.Add(new LayerDefinition
        {
            Name = "OTHER",
            FileStem = "OTHER",
            Kind = ElementKind.Device,
            ProcessingOrder = 35,
            AnchorNamespace = "OTHER",
        });
        AddDevice(scenario, scenario.AddLayer("OTHER"), 9, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (10, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ReferencesByMethod["anchor"].Should().Be(0);
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
        report.IssueCounts.Should().NotContainKey(ImportIssueCodes.AmbiguousAnchor);
    }

    [Fact]
    public async Task Run_AnotherNamespaceIsResolvedAgainstItsOwnLayer()
    {
        var scenario = NewScenario();
        scenario.Layers.Add(new LayerDefinition
        {
            Name = "OTHER",
            FileStem = "OTHER",
            Kind = ElementKind.Device,
            ProcessingOrder = 35,
            AnchorNamespace = "OTHER",
        });
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        AddDevice(scenario, scenario.AddLayer("OTHER"), 9, 0.2, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (10, 0)), [GroupRef("g", "OTHER")]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(9)).Topology.Should().ContainSingle();
        Feature(scenario, DeviceId(7)).Topology.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_GroupReferenceOnALayerThatDoesNotCollectGroupAnchors_HasNoNodes()
    {
        var scenario = NewScenario();
        scenario.Layers[0] = scenario.Layers[0] with { CollectsGroupAnchors = false };
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (10, 0)), Group("g"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ReferencesByMethod["anchor"].Should().Be(0);
        report.UnresolvedReferences.Should().Be(1);
    }

    [Fact]
    public async Task Run_ReferencesByMethod_AlwaysHasKeySnapAndAnchor()
    {
        var scenario = NewScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ReferencesByMethod.Keys.Should().BeEquivalentTo(["key", "snap", "anchor"]);
        report.ReferencesByMethod.Values.Should().OnlyContain(v => v == 0);
    }

    [Fact]
    public async Task Run_ReferencesByMethod_CountsKeySnapAndAnchorSeparately()
    {
        var scenario = NewScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 0, 0);
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0.1, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0)),
        [
            TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, "100"),
            TestData.Ref(ReferenceRole.End, TestData.PoleNamespace, null, snap: TestData.Point(0.2, 0)),
            GroupRef("g"),
        ]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ReferencesByMethod["key"].Should().Be(1);
        report.ReferencesByMethod["snap"].Should().Be(1);
        report.ReferencesByMethod["anchor"].Should().Be(1);
    }

    [Fact]
    public async Task Run_GeometryOnlyElementResolvedByAnAnchor_BecomesRecovered()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.2, 0), (50, 0)), Group("g"), completeness: Completeness.GeometryOnly);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().Be("recovered");
    }

    [Fact]
    public async Task Run_GeometryOnlyElementWithAnUnresolvedAnchor_StaysGeometryOnly()
    {
        var scenario = NewScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0)), Group("g"), completeness: Completeness.GeometryOnly);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().Be("geometry-only");
    }

    [Fact]
    public async Task Run_AnchorLayerThatIsNotSelected_StillProvidesCandidates()
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.2, 0), (50, 0)), Group("g"));

        var report = await scenario.RunAsync(r => r with { Layers = ["LINES"] }, CancellationToken.None);

        report.ReferencesByMethod["anchor"].Should().Be(1);
    }

    [Fact]
    public async Task Run_GroupsAreResolvedIndependently()
    {
        var scenario = NewScenario();
        var devices = scenario.AddLayer("DEVICES");
        AddDevice(scenario, devices, 7, 0, 0);
        AddDevice(scenario, devices, 8, 100, 0);
        AddDevice(scenario, devices, 9, 100.5, 0);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0.2, 0), (20, 0)), Group("one"));
        scenario.AddSegment(lines, 2, TestData.Line((100.2, 0), (120, 0)), Group("two"));
        scenario.AddSegment(lines, 3, TestData.Line((300, 0), (320, 0)), Group("three"));

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Single().TargetId.Should().Be(PipelineScenario.SegmentId(1));
        report.IssueCounts[ImportIssueCodes.AmbiguousAnchor].Should().Be(1);
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
        report.UnresolvedReferences.Should().Be(2);
        report.ReferencesByMethod["anchor"].Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_TheGroupOutcomeDoesNotDependOnTheElementOrder(bool reversed)
    {
        var scenario = NewScenario();
        var devices = scenario.AddLayer("DEVICES");
        AddDevice(scenario, devices, 7, 0, 0);
        AddDevice(scenario, devices, 8, 30, 0);
        var lines = scenario.AddLayer("LINES");
        long[] ids = reversed ? [3, 2, 1] : [1, 2, 3];
        foreach (var id in ids)
        {
            scenario.AddSegment(lines, id, TestData.Line((id * 10.0 - 10, 0.2), (id * 10.0, 0.2)), Group("g"));
        }

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.IssueCounts[ImportIssueCodes.AmbiguousAnchor].Should().Be(1);
        report.UnresolvedReferences.Should().Be(3);
        Feature(scenario, DeviceId(7)).Topology.Should().BeEmpty();
        Feature(scenario, DeviceId(8)).Topology.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_ASingleAnchorIsSharedByEveryElementWhateverTheOrder(bool reversed)
    {
        var scenario = NewScenario();
        AddDevice(scenario, scenario.AddLayer("DEVICES"), 7, 10, 0);
        var lines = scenario.AddLayer("LINES");
        long[] ids = reversed ? [3, 2, 1] : [1, 2, 3];
        foreach (var id in ids)
        {
            scenario.AddSegment(lines, id, TestData.Line((id * 10.0 - 10, 0.2), (id * 10.0, 0.2)), Group("g"));
        }

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, DeviceId(7)).Topology.Select(e => e.TargetId)
            .Should().BeEquivalentTo([PipelineScenario.SegmentId(1), PipelineScenario.SegmentId(2), PipelineScenario.SegmentId(3)]);
        report.ReferencesByMethod["anchor"].Should().Be(3);
        report.UnresolvedReferences.Should().Be(0);
    }
}
