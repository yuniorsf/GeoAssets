using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-229's AC: applying an <see cref="AssetMutationBatchRequestedEvent"/> updates the asset
/// store for every <see cref="AssetMutationKind"/>, and <see cref="Services.TopoGraph"/> reflects
/// the result on the very next call (it is stateless over whatever the provider currently holds —
/// nothing needs explicit invalidation).
/// </summary>
public class AssetMutationBatchApplierTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static AssetMutationBatchRequestedEvent Batch(IReadOnlyList<AssetFeatureMutationItem> mutations) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "catalog-primary",
            organizationId: Org,
            requestedByService: "GeoAssets.FieldService.Server",
            performedByUserId: "inspector-1",
            mutations: mutations,
            timeProvider: new FakeTimeProvider(FixedNowUtc));

    // ── UpsertFeature ─────────────────────────────────────────────────────────

    [Fact]
    public void Apply_UpsertFeature_NewFeature_AddsItWithGeometryAndAttributes()
    {
        var provider = new TestAssetProvider();
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutation = new AssetFeatureMutationItem(
            FeatureId: "pole-1",
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: "Pole 1",
            AssetTypeId: "pole",
            Description: "A pole",
            LayerId: "",
            Geometry: AssetGeometrySnapshot.Point(-43.2, -22.9),
            TopologyEdges: [],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["material"] = "concrete" });

        var result = applier.Apply(Batch([mutation]));

        var feature = provider.GetById("pole-1");
        feature.Should().NotBeNull();
        feature!.Properties.Name.Should().Be("Pole 1");
        feature.Properties.CustomAttributes["material"].Should().Be("concrete");
        feature.Geometry.Should().NotBeNull();
        result.AppliedItems.Should().ContainSingle().Which.MutationKind.Should().Be(AssetMutationKind.UpsertFeature);
    }

    [Fact]
    public void Apply_UpsertFeature_ExistingFeature_UpdatesInPlace()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "pole-1", Properties = new GeoFeatureProperties { Name = "Old Name" } });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutation = new AssetFeatureMutationItem(
            "pole-1", AssetMutationKind.UpsertFeature, "New Name", "pole", "", "",
            Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal));

        applier.Apply(Batch([mutation]));

        provider.GetById("pole-1")!.Properties.Name.Should().Be("New Name");
    }

    // ── UpdateAttributes ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_UpdateAttributes_MergesIntoExistingCustomAttributes()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "pole-1",
            Properties = new GeoFeatureProperties { CustomAttributes = { ["existing"] = "kept" } },
        });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutation = new AssetFeatureMutationItem(
            "pole-1", AssetMutationKind.UpdateAttributes, "", "pole", "", "",
            Geometry: null, TopologyEdges: [],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = "inspected" });

        applier.Apply(Batch([mutation]));

        var feature = provider.GetById("pole-1")!;
        feature.Properties.CustomAttributes["existing"].Should().Be("kept");
        feature.Properties.CustomAttributes["status"].Should().Be("inspected");
    }

    [Fact]
    public void Apply_UpdateAttributes_MissingFeature_Throws()
    {
        var provider = new TestAssetProvider();
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutation = new AssetFeatureMutationItem(
            "no-such-feature", AssetMutationKind.UpdateAttributes, "", "pole", "", "",
            Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal));

        var act = () => applier.Apply(Batch([mutation]));

        act.Should().Throw<KeyNotFoundException>();
    }

    // ── AddTopologyEdge / RemoveTopologyEdge — proves TopoGraph reflects the mutation ───────────

    [Fact]
    public void Apply_AddTopologyEdge_ThenTopoGraphGetNeighbors_ReflectsTheNewEdge()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "pole-1" });
        provider.Add(new GeoFeature { Id = "transformer-1" });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));

        provider.GetNeighbors("pole-1").Should().BeEmpty("no edge exists yet");

        var mutation = new AssetFeatureMutationItem(
            "pole-1", AssetMutationKind.AddTopologyEdge, "", "pole", "", "",
            Geometry: null,
            TopologyEdges: [new TopoEdgeSnapshot("transformer-1", "electric-flow")],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal));
        applier.Apply(Batch([mutation]));

        // The very next TopoGraph-backed call (no explicit invalidation step) already reflects it.
        provider.GetNeighbors("pole-1").Should().ContainSingle().Which.Id.Should().Be("transformer-1");
    }

    [Fact]
    public void Apply_RemoveTopologyEdge_ThenTopoGraphGetNeighbors_NoLongerReflectsIt()
    {
        var provider = new TestAssetProvider();
        var pole = new GeoFeature { Id = "pole-1" };
        pole.Topology.Add(new TopoEdge { TargetId = "transformer-1", Kind = "electric-flow" });
        provider.Add(pole);
        provider.Add(new GeoFeature { Id = "transformer-1" });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));

        provider.GetNeighbors("pole-1").Should().ContainSingle();

        var mutation = new AssetFeatureMutationItem(
            "pole-1", AssetMutationKind.RemoveTopologyEdge, "", "pole", "", "",
            Geometry: null,
            TopologyEdges: [new TopoEdgeSnapshot("transformer-1", "electric-flow")],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal));
        applier.Apply(Batch([mutation]));

        provider.GetNeighbors("pole-1").Should().BeEmpty();
    }

    // ── Decommission ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Decommission_RemovesTheFeature()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "pole-1" });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutation = new AssetFeatureMutationItem(
            "pole-1", AssetMutationKind.Decommission, "", "pole", "", "",
            Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal));

        applier.Apply(Batch([mutation]));

        provider.GetById("pole-1").Should().BeNull();
    }

    // ── Multi-item batch ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_MultiItemBatch_AppliesEachAndReturnsOneResultPerItem()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "pole-1" });
        var applier = new AssetMutationBatchApplier(provider, new FakeTimeProvider(FixedNowUtc));
        var mutations = new List<AssetFeatureMutationItem>
        {
            new("pole-1", AssetMutationKind.UpdateAttributes, "", "pole", "", "",
                Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1" }),
            new("pole-2", AssetMutationKind.UpsertFeature, "Pole 2", "pole", "", "",
                Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal)),
        };

        var result = applier.Apply(Batch(mutations));

        result.AppliedItems.Should().HaveCount(2);
        provider.GetById("pole-2").Should().NotBeNull();
    }
}
