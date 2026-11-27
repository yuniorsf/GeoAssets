using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static GeoAssets.Provider.PostgreSQL.Tests.QueryModeHarness;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresAssetMutationBatchApplier"/> (XD01-453): an upsert that creates a feature stores it with the batch's
/// organization, never as an organization-less (shared) row, and the batch's later items on the same id find it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetMutationBatchApplierNewFeatureTests(PostgresContainerFixture fixture)
{
    private static readonly DateTimeOffset Delivery = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly QueryModeHarness _harness = new(fixture);

    private PostgresAssetMutationBatchApplier NewSut() =>
        new(fixture.CreateOptions(), NullLoggerFactory.Instance, TimeProvider.System);

    private static AssetFeatureMutationItem Item(
        string featureId,
        AssetMutationKind kind,
        string assetType,
        string name = "",
        TopoEdgeSnapshot[]? edges = null,
        params (string Key, string Value)[] attributes) => new(
        FeatureId: featureId,
        MutationKind: kind,
        Name: name,
        AssetTypeId: assetType,
        Description: string.Empty,
        LayerId: string.Empty,
        Geometry: AssetGeometrySnapshot.Point(-38.5, -12.9),
        TopologyEdges: edges ?? [],
        CustomAttributes: attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    private static AssetMutationBatchRequestedEvent Batch(Guid organizationId, params AssetFeatureMutationItem[] mutations) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "default",
            organizationId: organizationId,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations: mutations,
            timeProvider: new FixedClock(Delivery),
            batchId: Guid.NewGuid(),
            sourceOrderId: "order-1",
            correlationId: "order-1");

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ApplyAsync_UpsertingANewFeature_StoresItWithTheBatchsOrganization()
    {
        var world = NewWorld();
        var id = NewId("new");

        var applied = await NewSut().ApplyAsync(
            Batch(world.Org, Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created")), CancellationToken.None);

        applied.AppliedItems.Should().ContainSingle();
        (await _harness.RowAsync(id)).Should().Be(new QueryRowSnapshot("created", world.Org));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 1_000_000);
        catalogs.For(world.Org).GetById(id).Should().NotBeNull();
        catalogs.For(world.Other).GetById(id).Should().BeNull("the feature is the batch organization's, not a shared one");
    }

    [Fact]
    public async Task ApplyAsync_UpsertThenUpdateAttributesOfANewFeature_FindsIt()
    {
        var world = NewWorld();
        var id = NewId("new-attrs");
        var batch = Batch(
            world.Org,
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created"),
            Item(id, AssetMutationKind.UpdateAttributes, world.AssetType, attributes: ("occupancyClass", "High")));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 1_000_000);
        var feature = catalogs.For(world.Org).GetById(id);
        feature!.Properties.OrganizationId.Should().Be(world.Org);
        feature.Properties.CustomAttributes.Should().Contain("occupancyClass", "High");
    }

    [Fact]
    public async Task ApplyAsync_UpsertThenAddTopologyEdgeOfANewFeature_FindsIt()
    {
        var world = NewWorld();
        var target = Feature(world, NewId("edge-target"), world.Org, 0.001);
        await _harness.SeedAsync(target);
        var id = NewId("new-edge");
        var batch = Batch(
            world.Org,
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created"),
            Item(id, AssetMutationKind.AddTopologyEdge, world.AssetType, edges: [new TopoEdgeSnapshot(target.Id, "feeds")]));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 1_000_000);
        catalogs.For(world.Org).GetById(id)!.Topology
            .Should().ContainSingle(e => e.TargetId == target.Id && e.Kind == "feeds");
    }

    [Fact]
    public async Task ApplyAsync_UpsertingANewFeatureTwice_UpdatesTheFirstInsert()
    {
        var world = NewWorld();
        var id = NewId("new-twice");
        var batch = Batch(
            world.Org,
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "one"),
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "two"));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        (await _harness.RowAsync(id)).Should().Be(new QueryRowSnapshot("two", world.Org));
    }

    [Fact]
    public async Task ApplyAsync_UpsertThenDecommissionOfANewFeature_LeavesNoRow()
    {
        var world = NewWorld();
        var id = NewId("new-gone");
        var batch = Batch(
            world.Org,
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created"),
            Item(id, AssetMutationKind.Decommission, world.AssetType));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        (await _harness.RowAsync(id)).Should().BeNull("the decommission found the feature the upsert created and deleted it");
    }

    [Fact]
    public async Task ApplyAsync_ALaterItemFails_TheNewFeatureIsNotStored()
    {
        var world = NewWorld();
        var id = NewId("new-rollback");
        var missing = NewId("missing");
        var batch = Batch(
            world.Org,
            Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created"),
            Item(missing, AssetMutationKind.UpdateAttributes, world.AssetType, attributes: ("occupancyClass", "High")));

        var act = () => NewSut().ApplyAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<AssetMutationFeatureNotFoundException>();
        (await _harness.RowAsync(id)).Should().BeNull("the insert went through the batch's transaction, which rolled back");
    }

    [Fact]
    public async Task ApplyAsync_TheSameBatchDeliveredTwice_CreatesTheFeatureOnce()
    {
        var world = NewWorld();
        var id = NewId("new-redelivered");
        var batch = Batch(world.Org, Item(id, AssetMutationKind.UpsertFeature, world.AssetType, "created"));

        await NewSut().ApplyAsync(batch, CancellationToken.None);
        await _harness.RenameDirectAsync(id, "renamed after the first delivery");
        await NewSut().ApplyAsync(batch, CancellationToken.None);

        (await _harness.RowAsync(id)).Should().Be(
            new QueryRowSnapshot("renamed after the first delivery", world.Org),
            "the redelivery found the batch record the first delivery committed with the feature, and wrote nothing");
    }
}
