using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static GeoAssets.Provider.PostgreSQL.Tests.QueryModeHarness;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresAssetMutationBatchApplier"/> (XD01-449): its per-batch provider reads the store through the batch's own
/// transaction instead of loading the organization's catalog, so a feature the batch itself wrote is read back by the
/// mutations after it, and an organization with many features is applied to as before.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetMutationBatchApplierStoreReadsTests(PostgresContainerFixture fixture)
{
    private static readonly DateTimeOffset Delivery = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly QueryModeHarness _harness = new(fixture);

    private PostgresAssetMutationBatchApplier NewSut() =>
        new(fixture.CreateOptions(), NullLoggerFactory.Instance, TimeProvider.System);

    private static AssetFeatureMutationItem Item(
        string featureId, AssetMutationKind kind, string assetType, string name = "", params (string Key, string Value)[] attributes) => new(
        FeatureId: featureId,
        MutationKind: kind,
        Name: name,
        AssetTypeId: assetType,
        Description: string.Empty,
        LayerId: string.Empty,
        Geometry: AssetGeometrySnapshot.Point(-38.5, -12.9),
        TopologyEdges: [],
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
    public async Task ApplyAsync_TwoItemsOnOneFeature_TheSecondReadsTheFirstsUncommittedWrite()
    {
        var world = NewWorld();
        var pole = Feature(world, NewId("two-items"), world.Org, 0.001);
        await _harness.SeedAsync(pole);
        var batch = Batch(
            world.Org,
            Item(pole.Id, AssetMutationKind.UpdateAttributes, world.AssetType, attributes: ("occupancyClass", "High")),
            Item(pole.Id, AssetMutationKind.UpdateAttributes, world.AssetType, attributes: ("occupantCount", "6")));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 1_000_000);
        catalogs.For(world.Org).GetById(pole.Id)!.Properties.CustomAttributes
            .Should().Contain("occupancyClass", "High", "the first item's write")
            .And.Contain("occupantCount", "6", "the second item read the first's uncommitted write and kept it");
    }

    [Fact]
    public async Task ApplyAsync_UpsertingAnExistingFeatureTwice_UpdatesIt_AndKeepsTheLastValues()
    {
        var world = NewWorld();
        var pole = Feature(world, NewId("twice"), world.Org, 0.001);
        await _harness.SeedAsync(pole);
        var batch = Batch(
            world.Org,
            Item(pole.Id, AssetMutationKind.UpsertFeature, world.AssetType, "one"),
            Item(pole.Id, AssetMutationKind.UpsertFeature, world.AssetType, "two"));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.AppliedItems.Should().HaveCount(2);
        (await _harness.RowAsync(pole.Id))!.Should().Be(new QueryRowSnapshot("two", world.Org));
    }

    [Fact]
    public async Task ApplyAsync_ToAnOrganizationWithManyFeatures_UpdatesOnlyTheNamedOnes()
    {
        var world = NewWorld();
        var others = Enumerable.Range(0, 60).Select(i => Feature(world, NewId("bulk"), world.Org, 0.0001 * (i + 1))).ToArray();
        var target = others[17];
        await _harness.SeedAsync(others);
        var batch = Batch(world.Org, Item(target.Id, AssetMutationKind.UpdateAttributes, world.AssetType, attributes: ("occupancyClass", "Low")));

        await NewSut().ApplyAsync(batch, CancellationToken.None);

        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 1_000_000);
        var view = catalogs.For(world.Org);
        view.GetById(target.Id)!.Properties.CustomAttributes.Should().Contain("occupancyClass", "Low");
        view.GetById(others[18].Id)!.Properties.CustomAttributes.Should().NotContainKey("occupancyClass");
        view.GetByAssetType(world.AssetType).Should().HaveCount(60);
    }
}
