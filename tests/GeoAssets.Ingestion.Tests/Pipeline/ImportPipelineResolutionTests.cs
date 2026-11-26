using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineResolutionTests
{
    private static GeoFeature Feature(PipelineScenario scenario, string id) => scenario.Sink.Features.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    private static ElementReference PoleRef(ReferenceRole role, long id, int? node = null, Point? snap = null) =>
        TestData.Ref(role, TestData.PoleNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture), node, snap);

    [Fact]
    public async Task Run_ReferenceWithAKnownKey_ResolvesByKey()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddPole(scenario.AddLayer("POLES", "B"), 200, 100, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (100, 0)),
            [PoleRef(ReferenceRole.Start, 100, 1), PoleRef(ReferenceRole.End, 200, 2)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edges = Feature(scenario, PipelineScenario.SegmentId(1)).Topology;
        edges.Should().HaveCount(2);
        edges.Select(e => e.TargetId).Should().BeEquivalentTo([TestData.PoleId(100), TestData.PoleId(200)]);
        edges.Should().OnlyContain(e => e.Metadata["resolution"] == "key");
        report.ReferencesByMethod["key"].Should().Be(2);
        report.UnresolvedReferences.Should().Be(0);
    }

    [Fact]
    public async Task Run_ReferenceToAnotherRegion_ResolvesEvenWhenThatRegionIsNotSelected()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES", "North"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES", "South"), 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.Start, 100)]);

        await scenario.RunAsync(r => r with { Regions = ["South"] }, CancellationToken.None);

        scenario.Sink.Features.Should().ContainSingle();
        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single().TargetId.Should().Be(TestData.PoleId(100));
    }

    [Fact]
    public async Task Run_ReferenceToAnIndexedLayerThatIsNotSelected_StillResolves()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.Start, 100)]);

        await scenario.RunAsync(r => r with { Layers = ["LINES"] }, CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_UnknownKeyWithSnapPointWithinTolerance_ResolvesBySnapWithDistance()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.3, 0.4), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, "999", snap: TestData.Point(0.3, 0.4))]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single();
        edge.TargetId.Should().Be(TestData.PoleId(100));
        edge.Metadata["resolution"].Should().Be("snap");
        edge.Metadata["distance"].Should().Be("0.50");
        report.ReferencesByMethod["snap"].Should().Be(1);
    }

    [Fact]
    public async Task Run_NullKeyWithSnapPoint_ResolvesBySnap()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 10, 10);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((10, 10.25), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(10, 10.25))]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single().Metadata["distance"].Should().Be("0.25");
    }

    [Fact]
    public async Task Run_SnapBeyondTheDeclaringLayersTolerance_IsADanglingReferenceWithoutAnEdge()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((1.5, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(1.5, 0))]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().BeEmpty();
        report.UnresolvedReferences.Should().Be(1);
        report.IssueCounts[ImportIssueCodes.DanglingReference].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.DanglingReference, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Warning);
    }

    [Fact]
    public async Task Run_SnapUsesTheDeclaringLayersTolerance()
    {
        var scenario = new PipelineScenario();
        scenario.Layers[0] = scenario.Layers[0] with { SnapToleranceMeters = 3.0 };
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((1.5, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(1.5, 0))]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single().Metadata["distance"].Should().Be("1.50");
        report.UnresolvedReferences.Should().Be(0);
    }

    [Fact]
    public async Task Run_SnapPicksTheNearestSnapTarget()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 0, 0);
        scenario.AddPole(poles, 101, 0.6, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.5, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(0.55, 0))]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single().TargetId.Should().Be(TestData.PoleId(101));
    }

    [Fact]
    public async Task Run_KeyTakesPrecedenceOverSnap()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 0, 0);
        scenario.AddPole(poles, 101, 4, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((4, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, "100", snap: TestData.Point(4, 0))]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single();
        edge.TargetId.Should().Be(TestData.PoleId(100));
        edge.Metadata["resolution"].Should().Be("key");
    }

    [Fact]
    public async Task Run_UnknownKeyWithoutSnapPoint_IsDangling()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.Start, 404)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.UnresolvedReferences.Should().Be(1);
        report.ReferencesByMethod.Values.Sum().Should().Be(0);
        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_ZeroOrBlankIndexValues_AreNotIndexed()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddElement(poles,
            scope => TestData.Element(TestData.PoleKey(0), ElementKind.Structure, "Pole", TestData.Point(0, 0), scope),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = 0L });
        scenario.AddElement(poles,
            scope => TestData.Element(TestData.PoleKey(7), ElementKind.Structure, "Pole", TestData.Point(9, 9), scope),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = "  " });
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (9, 9)),
            [PoleRef(ReferenceRole.Start, 0), PoleRef(ReferenceRole.End, 7)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.UnresolvedReferences.Should().Be(2);
    }

    [Fact]
    public async Task Run_ReferenceToADeviceNamespace_ResolvesThroughThatLayersIndex()
    {
        var scenario = new PipelineScenario();
        var devices = scenario.AddLayer("DEVICES");
        scenario.AddElement(devices,
            scope => TestData.Element(new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"), ElementKind.Device, "Device", TestData.Point(3, 3), scope),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = 5L });
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((3, 3), (9, 9)),
            [TestData.Ref(ReferenceRole.MemberOf, TestData.DeviceNamespace, "5", qualifier: "feeder-head")]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var expected = DeterministicFeatureId.For(new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"));
        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single().TargetId.Should().Be(expected);
    }

    [Fact]
    public async Task Run_GeometryOnlyElementResolvedBySnap_BecomesRecovered()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.2, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(0.2, 0))],
            completeness: Completeness.GeometryOnly);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().Be("recovered");
    }

    [Fact]
    public async Task Run_GeometryOnlyElementWithoutResolution_StaysGeometryOnly()
    {
        var scenario = new PipelineScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(0, 0))],
            completeness: Completeness.GeometryOnly);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().Be("geometry-only");
    }

    [Fact]
    public async Task Run_FullElementResolvedBySnap_StaysFull()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.2, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(0.2, 0))]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Properties.CustomAttributes[FeatureImportAttributes.Completeness].Should().Be("full");
    }

    [Fact]
    public async Task Run_GeometryOnlySnapTargets_ParticipateInSnappingWhenIncluded()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES", verdict: LayerVerdict.GeometryOnly), 100, 0, 0, completeness: Completeness.GeometryOnly);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0.2, 0), (50, 0)),
            [TestData.Ref(ReferenceRole.Start, TestData.PoleNamespace, null, snap: TestData.Point(0.2, 0))]);

        var report = await scenario.RunAsync(r => r with { IncludeGeometryOnly = true }, CancellationToken.None);

        report.ReferencesByMethod["snap"].Should().Be(1);
    }

    [Fact]
    public async Task Run_SameElementKeyTwice_ImportsTheFirstAndFlagsTheDuplicate()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 0, 0, name: "first");
        scenario.AddPole(poles, 100, 9, 9, name: "second");

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Should().ContainSingle().Which.Properties.Name.Should().Be("first");
        report.IssueCounts[ImportIssueCodes.DuplicateElement].Should().Be(1);
        report.IssueSamples.Single(i => string.Equals(i.Code, ImportIssueCodes.DuplicateElement, StringComparison.Ordinal)).Severity.Should().Be(IssueSeverity.Warning);
        report.ElementsByAssetClass["Pole"].Should().Be(1);
    }
}
