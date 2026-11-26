using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Tests.Fakes;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Pipeline;

public sealed class ImportPipelineProjectionTests
{
    private static GeoFeature Feature(PipelineScenario scenario, string id) => scenario.Sink.Features.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    private static ElementReference PoleRef(ReferenceRole role, long id, int? node = null, string? qualifier = null, Point? snap = null) =>
        TestData.Ref(role, TestData.PoleNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture), node, snap, qualifier);

    [Fact]
    public async Task Run_ProjectsIdNameAssetTypeOrganizationAndSrid()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 12.5, 7.25, name: "Poste 100");

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var feature = scenario.Sink.Features.Single();
        feature.Id.Should().Be(DeterministicFeatureId.For(TestData.PoleKey(100)));
        feature.Properties.Name.Should().Be("Poste 100");
        feature.Properties.AssetTypeId.Should().Be("type-Pole");
        feature.Properties.OrganizationId.Should().Be(Guid.Parse("0000000a-0000-0000-0000-000000000001"));
        feature.Properties.Srid.Should().Be(4326);
        feature.Geometry!.GeometryType.Should().Be(GeometryType.Point);
        feature.Geometry.Srid.Should().Be(4326);
        var point = (Point)feature.Geometry.NtsGeometry;
        point.X.Should().Be(12.5);
        point.Y.Should().Be(7.25);
    }

    [Fact]
    public async Task Run_ElementWithoutAName_GetsTheClassAndNaturalKey()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Single().Properties.Name.Should().Be("Pole 100");
    }

    [Fact]
    public async Task Run_LongName_IsTruncatedTo256Characters()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0, name: new string('n', 400));

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        scenario.Sink.Features.Single().Properties.Name.Should().HaveLength(256);
    }

    [Fact]
    public async Task Run_SegmentProjectsAsALineString()
    {
        var scenario = new PipelineScenario();
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (3, 4)), []);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var feature = scenario.Sink.Features.Single();
        feature.Geometry!.GeometryType.Should().Be(GeometryType.LineString);
        feature.Properties.AssetTypeId.Should().Be("type-Segment");
    }

    [Fact]
    public async Task Run_StampsProvenanceAndElementAttributes()
    {
        var scenario = new PipelineScenario();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["NU_ALTURA"] = "11", ["DE_BARRAMENTO"] = "ABC" };
        scenario.AddPole(scenario.AddLayer("POLES", "Sul"), 100, 0, 0, attributes: attributes);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var custom = scenario.Sink.Features.Single().Properties.CustomAttributes;
        custom[FeatureImportAttributes.Profile].Should().Be(TestData.ProfileId);
        custom[FeatureImportAttributes.Key].Should().Be(TestData.PoleKey(100).ToString());
        custom[FeatureImportAttributes.Scope].Should().Be("net/Sul/POLES");
        custom[FeatureImportAttributes.Completeness].Should().Be("full");
        custom[FeatureImportAttributes.PositionAccuracy].Should().Be("none");
        custom["NU_ALTURA"].Should().Be("11");
        custom["DE_BARRAMENTO"].Should().Be("ABC");
    }

    [Fact]
    public async Task Run_ReportCountsProjectedElementsPerAssetClass()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 1, 0, 0);
        scenario.AddPole(poles, 2, 1, 1);
        scenario.AddPole(scenario.AddLayer("DEVICES"), 3, 2, 2, assetClass: "Device");

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        report.ElementsByAssetClass.Should().BeEquivalentTo(new Dictionary<string, long>(StringComparer.Ordinal) { ["Pole"] = 2, ["Device"] = 1 });
    }

    [Fact]
    public async Task Run_StartAndEndReferences_BecomeConnectedToEdgesWithTerminalAndNode()
    {
        var scenario = new PipelineScenario();
        var poles = scenario.AddLayer("POLES");
        scenario.AddPole(poles, 100, 0, 0);
        scenario.AddPole(poles, 200, 10, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (10, 0)),
            [PoleRef(ReferenceRole.Start, 100, 3), PoleRef(ReferenceRole.End, 200)]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edges = Feature(scenario, PipelineScenario.SegmentId(1)).Topology;
        var start = edges.Single(e => string.Equals(e.TargetId, TestData.PoleId(100), StringComparison.Ordinal));
        start.Kind.Should().Be("connected-to");
        start.Weight.Should().Be(1);
        start.Metadata["terminal"].Should().Be("1");
        start.Metadata["node"].Should().Be("3");
        var end = edges.Single(e => string.Equals(e.TargetId, TestData.PoleId(200), StringComparison.Ordinal));
        end.Kind.Should().Be("connected-to");
        end.Metadata["terminal"].Should().Be("2");
        end.Metadata.Should().NotContainKey("node");
    }

    [Fact]
    public async Task Run_LocationReference_BecomesAMountedOnEdge()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddElement(scenario.AddLayer("DEVICES"), scope => TestData.Element(
            new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"), ElementKind.Device, "Device", TestData.Point(0, 0), scope,
            references: [PoleRef(ReferenceRole.Location, 100, 4)]));

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, DeterministicFeatureId.For(new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"))).Topology.Single();
        edge.Kind.Should().Be("mounted-on");
        edge.TargetId.Should().Be(TestData.PoleId(100));
        edge.Metadata["resolution"].Should().Be("key");
    }

    [Fact]
    public async Task Run_ConnectionReference_BecomesAConnectedToEdgeWithNode()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddElement(scenario.AddLayer("DEVICES"), scope => TestData.Element(
            new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"), ElementKind.Device, "Device", TestData.Point(0, 0), scope,
            references: [PoleRef(ReferenceRole.Connection, 100, 2)]));

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, DeterministicFeatureId.For(new SourceKey(TestData.ProfileId, TestData.DeviceNamespace, "5"))).Topology.Single();
        edge.Kind.Should().Be("connected-to");
        edge.Metadata["node"].Should().Be("2");
        edge.Metadata.Should().NotContainKey("terminal");
    }

    [Fact]
    public async Task Run_MemberOfReference_BecomesAMemberOfEdgeWithGroup()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (1, 0)),
            [PoleRef(ReferenceRole.MemberOf, 100, qualifier: "feeder-head")]);

        await scenario.RunAsync(cancellationToken: CancellationToken.None);

        var edge = Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Single();
        edge.Kind.Should().Be("member-of");
        edge.Metadata["group"].Should().Be("feeder-head");
    }

    [Fact]
    public async Task Run_FedByReference_IsReversedIntoAFeedsEdgeOnTheLaterProjectedTarget()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.FedBy, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().BeEmpty();
        var edge = Feature(scenario, TestData.PoleId(100)).Topology.Single();
        edge.Kind.Should().Be("feeds");
        edge.TargetId.Should().Be(PipelineScenario.SegmentId(1));
        edge.Metadata["resolution"].Should().Be("key");
        report.ReferencesByMethod["key"].Should().Be(1);
    }

    [Fact]
    public async Task Run_FedByReferenceWhoseTargetWasAlreadyProjected_IsDroppedWithAnInfoIssue()
    {
        var scenario = new PipelineScenario();
        scenario.Layers[0] = scenario.Layers[0] with { ProcessingOrder = 90 };
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        scenario.AddSegment(scenario.AddLayer("LINES"), 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.FedBy, 100)]);

        var report = await scenario.RunAsync(cancellationToken: CancellationToken.None);

        Feature(scenario, TestData.PoleId(100)).Topology.Should().BeEmpty();
        Feature(scenario, PipelineScenario.SegmentId(1)).Topology.Should().BeEmpty();
        report.IssueSamples.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.DanglingReference, StringComparison.Ordinal) && i.Severity == IssueSeverity.Info);
    }

    [Fact]
    public async Task Run_SeveralFedByDeclarers_AllAttachFeedsEdgesToTheTarget()
    {
        var scenario = new PipelineScenario();
        scenario.AddPole(scenario.AddLayer("POLES"), 100, 0, 0);
        var lines = scenario.AddLayer("LINES");
        scenario.AddSegment(lines, 1, TestData.Line((0, 0), (1, 0)), [PoleRef(ReferenceRole.FedBy, 100)]);
        scenario.AddSegment(lines, 2, TestData.Line((0, 0), (2, 0)), [PoleRef(ReferenceRole.FedBy, 100)]);

        await scenario.RunAsync(r => r with { ChunkSize = 1 }, CancellationToken.None);

        Feature(scenario, TestData.PoleId(100)).Topology.Select(e => e.TargetId)
            .Should().BeEquivalentTo([PipelineScenario.SegmentId(1), PipelineScenario.SegmentId(2)]);
    }
}
