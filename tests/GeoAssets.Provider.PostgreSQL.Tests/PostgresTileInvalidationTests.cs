using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// After-commit dirty-tile invalidation (XD01-432) of every feature writer of the provider, against the collection's
/// migrated PostGIS instance: <see cref="PostgresAssetProvider"/>'s writes, <see cref="PostgresAssetMutationBatchApplier"/>'s
/// batches and <see cref="PostgresFeatureBulkImporter"/>'s chunks and completions each hand <see cref="ITileInvalidation"/>
/// the boxes the written features had before and have after the write — once, after the commit (the invalidation sees
/// the write), never for a write that failed or rolled back — and an invalidation that throws never fails the write.
/// Each test uses its own organizations, ids and map area, so nothing is truncated.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTileInvalidationTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly List<string> _featureIds = [];
    private readonly List<Guid> _assetTypeIds = [];
    private readonly double _lon = RandomNumberGenerator.GetInt32(-170, 170) + 0.5;
    private readonly double _lat = RandomNumberGenerator.GetInt32(-60, 60) + 0.5;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(e => _featureIds.Contains(e.Id)).ExecuteDeleteAsync();
        await db.AssetTypes.Where(t => _assetTypeIds.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private string NewId(string prefix)
    {
        var id = $"{prefix}-{Guid.NewGuid():N}"[..30];
        _featureIds.Add(id);
        return id;
    }

    private GeoFeature Feature(string id, double offset, Guid organizationId = default, string assetTypeId = "type-pole") => new()
    {
        Id = id,
        Geometry = new GeoPoint(_lon + offset, _lat + offset),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = assetTypeId,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = "P-1" },
        },
    };

    private DirtyTileRegion PointRegion(double offset) =>
        new(new BoundingBox(_lon + offset, _lat + offset, _lon + offset, _lat + offset));

    private PostgresAssetProvider NewProvider(ITileInvalidation? invalidation = null)
    {
        var options = fixture.CreateOptions();
        return new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System, invalidation);
    }

    private async Task SeedAsync(params GeoFeature[] features)
    {
        await using var provider = NewProvider();
        provider.AddRange(features);
    }

    /// <summary>The stored longitude of <paramref name="id"/>'s point, read by a context of its own; <c>null</c> when it is not stored.</summary>
    private double? StoredLon(string id)
    {
        using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return db.GeoEntities.IgnoreQueryFilters().Where(e => e.Id == id).Select(e => (double?)e.Geom!.Centroid.X).SingleOrDefault();
    }

    // ── PostgresAssetProvider ─────────────────────────────────────────────────

    [Fact]
    public async Task Add_HandsTheNewFeaturesBox_Once_AfterTheCommit()
    {
        var id = NewId("add");
        double? seen = null;
        var invalidation = new RecordingTileInvalidation(_ => seen = StoredLon(id));
        await using var provider = NewProvider(invalidation);

        provider.Add(Feature(id, 0.001));

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001));
        seen.Should().BeApproximately(_lon + 0.001, 1e-9, "the invalidation runs once the write is committed");
    }

    [Fact]
    public async Task Update_OfAMovedFeature_HandsItsBoxesBeforeAndAfter()
    {
        var id = NewId("move");
        await SeedAsync(Feature(id, 0.001));
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        provider.Update(Feature(id, 0.002));

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001), PointRegion(0.002));
    }

    [Fact]
    public async Task Update_InPlace_HandsItsOneBox()
    {
        var id = NewId("stay");
        await SeedAsync(Feature(id, 0.001));
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        provider.Update(Feature(id, 0.001));

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001));
    }

    [Fact]
    public async Task Delete_HandsTheRemovedFeaturesBox()
    {
        var id = NewId("delete");
        await SeedAsync(Feature(id, 0.003));
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        provider.Delete(id);

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.003));
    }

    [Fact]
    public async Task AddRange_HandsTheUpdatedFeaturesOldAndNewBoxes_AndTheNewFeaturesBoxes()
    {
        var existing = NewId("existing");
        var added = NewId("added");
        await SeedAsync(Feature(existing, 0.001));
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        provider.AddRange([Feature(existing, 0.002), Feature(added, 0.004)]);

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001), PointRegion(0.002), PointRegion(0.004));
    }

    [Fact]
    public async Task AWriteOfManyFeatures_IsOneRegionOfTheirUnion()
    {
        var features = Enumerable.Range(0, DirtyTileRegionsLimit + 1).Select(i => Feature(NewId("many"), i * 0.00001)).ToArray();
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        provider.AddRange(features);

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(
            new DirtyTileRegion(new BoundingBox(_lon, _lat, _lon + (DirtyTileRegionsLimit * 0.00001), _lat + (DirtyTileRegionsLimit * 0.00001))));
    }

    /// <summary>The provider's <c>DirtyTileRegions.MaxRegionsPerWrite</c> (internal).</summary>
    private const int DirtyTileRegionsLimit = 256;

    [Fact]
    public async Task Clear_AndLoadAll_OfAnOrganization_HandTheBoxesOfWhatTheyRemoveAndWrite()
    {
        var organization = Guid.NewGuid();
        var first = NewId("org-first");
        var second = NewId("org-second");
        var loaded = NewId("org-loaded");
        await SeedAsync(Feature(first, 0.001, organization), Feature(second, 0.002, organization));
        var invalidation = new RecordingTileInvalidation();
        await using var shared = new ValidatingAssetProvider(NewProvider());
        await using var catalogs = new PostgresOrganizationAssetCatalogs(
            fixture.CreateOptions(), shared, NullLoggerFactory.Instance, TimeProvider.System, invalidation);

        catalogs.For(organization).LoadAll([Feature(loaded, 0.005, organization)]);
        catalogs.For(organization).Clear();

        invalidation.Calls.Should().HaveCount(2);
        invalidation.Calls[0].Should().BeEquivalentTo([PointRegion(0.001), PointRegion(0.002), PointRegion(0.005)]);
        invalidation.Calls[1].Should().Equal(PointRegion(0.005));
    }

    [Fact]
    public async Task AFailedWrite_HandsNothing()
    {
        var id = NewId("dup");
        await SeedAsync(Feature(id, 0.001));
        var invalidation = new RecordingTileInvalidation();
        await using var provider = NewProvider(invalidation);

        var act = () => provider.Add(Feature(id, 0.002));

        act.Should().Throw<DbUpdateException>();
        invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AWriteRefusedForAnotherOrganization_HandsNothing()
    {
        var owner = Guid.NewGuid();
        var id = NewId("foreign");
        await SeedAsync(Feature(id, 0.001, owner));
        var invalidation = new RecordingTileInvalidation();
        await using var shared = new ValidatingAssetProvider(NewProvider());
        await using var catalogs = new PostgresOrganizationAssetCatalogs(
            fixture.CreateOptions(), shared, NullLoggerFactory.Instance, TimeProvider.System, invalidation);

        var act = () => catalogs.For(Guid.NewGuid()).Update(Feature(id, 0.002));

        act.Should().Throw<ForeignGeoFeatureWriteException>();
        invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AThrowingInvalidation_NeverFailsTheWrite_AndIsCountedAsFailed()
    {
        var id = NewId("throws");
        var failures = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Name, "geoassets.tiles.invalidations", StringComparison.Ordinal)) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var failed = false;
            var provider = false;
            foreach (var tag in tags)
            {
                failed |= tag is { Key: "outcome", Value: "failed" };
                provider |= tag is { Key: "writer", Value: "provider" };
            }

            if (failed && provider) Interlocked.Add(ref failures, value);
        });
        listener.Start();
        var invalidation = new RecordingTileInvalidation(fail: true);
        await using var sut = NewProvider(invalidation);

        var act = () => sut.Add(Feature(id, 0.001));

        act.Should().NotThrow();
        invalidation.Calls.Should().ContainSingle();
        StoredLon(id).Should().BeApproximately(_lon + 0.001, 1e-9);
        Interlocked.Read(ref failures).Should().BeGreaterThanOrEqualTo(1);
    }

    // ── PostgresAssetMutationBatchApplier ─────────────────────────────────────

    private static AssetFeatureMutationItem SetAttributes(string featureId, string value) => new(
        FeatureId: featureId,
        MutationKind: AssetMutationKind.UpdateAttributes,
        Name: string.Empty,
        AssetTypeId: "type-pole",
        Description: string.Empty,
        LayerId: string.Empty,
        Geometry: null,
        TopologyEdges: [],
        CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["occupancyClass"] = value });

    private static AssetMutationBatchRequestedEvent Batch(Guid organizationId, params AssetFeatureMutationItem[] mutations) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "default",
            organizationId: organizationId,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations: mutations,
            timeProvider: new FixedClock(Now),
            batchId: Guid.NewGuid(),
            sourceOrderId: "order-1",
            correlationId: "order-1");

    private PostgresAssetMutationBatchApplier NewApplier(ITileInvalidation invalidation) =>
        new(fixture.CreateOptions(), NullLoggerFactory.Instance, new FixedClock(Now), invalidation);

    private string? StoredAttribute(string id)
    {
        using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return db.GeoEntityAttributes.Where(a => a.EntityId == id && a.Key == "occupancyClass").Select(a => a.Value).SingleOrDefault();
    }

    [Fact]
    public async Task ABatch_HandsItsFeaturesBoxes_Once_AfterItsCommit_AndNotAgainWhenRedelivered()
    {
        var organization = Guid.NewGuid();
        var first = NewId("batch-1");
        var second = NewId("batch-2");
        await SeedAsync(Feature(first, 0.001, organization), Feature(second, 0.002, organization));
        string? seen = null;
        var invalidation = new RecordingTileInvalidation(_ => seen = StoredAttribute(second));
        var sut = NewApplier(invalidation);
        var batch = Batch(organization, SetAttributes(first, "High"), SetAttributes(second, "Low"));

        await sut.ApplyAsync(batch, CancellationToken.None);
        await sut.ApplyAsync(batch, CancellationToken.None);

        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001), PointRegion(0.002));
        seen.Should().Be("Low", "the batch's last write is committed when the invalidation runs");
    }

    [Fact]
    public async Task ABatchThatRollsBack_HandsNothing()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("rollback");
        await SeedAsync(Feature(pole, 0.001, organization));
        var invalidation = new RecordingTileInvalidation();
        var batch = Batch(organization, SetAttributes(pole, "High"), SetAttributes(NewId("missing"), "Low"));

        var act = () => NewApplier(invalidation).ApplyAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AThrowingInvalidation_NeverFailsTheBatch()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("batch-throws");
        await SeedAsync(Feature(pole, 0.001, organization));
        var invalidation = new RecordingTileInvalidation(fail: true);

        var act = () => NewApplier(invalidation).ApplyAsync(Batch(organization, SetAttributes(pole, "High")), CancellationToken.None);

        await act.Should().NotThrowAsync();
        invalidation.Calls.Should().ContainSingle();
        StoredAttribute(pole).Should().Be("High");
    }

    // ── PostgresFeatureBulkImporter ───────────────────────────────────────────

    private async Task<string> NewPointTypeAsync()
    {
        var type = new AssetType { Name = $"tiles-import-{Guid.NewGuid():N}", Color = "#123456" };
        _assetTypeIds.Add(type.Id);
        await using var provider = NewProvider();
        provider.AddAssetType(type);
        return type.Id.ToString();
    }

    private PostgresFeatureBulkImporter NewImporter(ITileInvalidation invalidation) =>
        new(fixture.CreateOptions(), new FixedClock(Now), catalogs: null, invalidation, NullLogger<PostgresFeatureBulkImporter>.Instance);

    [Fact]
    public async Task AnImportChunk_HandsItsUpdatedFeaturesOldAndNewBoxes_AndItsNewFeaturesBoxes_AfterItsCommit()
    {
        var organization = Guid.NewGuid();
        var type = await NewPointTypeAsync();
        var existing = NewId("imp-existing");
        var added = NewId("imp-added");
        await SeedAsync(Feature(existing, 0.001, organization, type));
        double? seen = null;
        var invalidation = new RecordingTileInvalidation(_ => seen = StoredLon(added));

        var result = await NewImporter(invalidation).ImportChunkAsync(
            new FeatureImportChunkRequest(organization, "import-1", "scope-1", [Feature(existing, 0.002, organization, type), Feature(added, 0.004, organization, type)]),
            CancellationToken.None);

        result.Inserted.Should().Be(1);
        invalidation.Calls.Should().ContainSingle().Which.Should().Equal(PointRegion(0.001), PointRegion(0.002), PointRegion(0.004));
        seen.Should().BeApproximately(_lon + 0.004, 1e-9);
    }

    [Fact]
    public async Task AnImportChunkWritingNothing_HandsNothing()
    {
        var invalidation = new RecordingTileInvalidation();

        await NewImporter(invalidation).ImportChunkAsync(
            new FeatureImportChunkRequest(Guid.NewGuid(), "import-1", "scope-1", [Feature(NewId("imp-unknown"), 0.001, assetTypeId: Guid.NewGuid().ToString())]),
            CancellationToken.None);

        invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AReplacingCompletion_HandsTheBoxesOfTheFeaturesItRemoves()
    {
        var organization = Guid.NewGuid();
        var type = await NewPointTypeAsync();
        var stale = NewId("imp-stale");
        var invalidation = new RecordingTileInvalidation();
        var sut = NewImporter(invalidation);
        await sut.ImportChunkAsync(new FeatureImportChunkRequest(organization, "import-1", "scope-1", [Feature(stale, 0.003, organization, type)]), CancellationToken.None);

        var completion = await sut.CompleteAsync(
            new FeatureImportCompletion(organization, "import-2", "scope-1", FeatureImportCompletionMode.ReplaceScope), CancellationToken.None);

        completion.Removed.Should().Be(1);
        invalidation.Calls.Should().HaveCount(2);
        invalidation.Calls[1].Should().Equal(PointRegion(0.003));
        StoredLon(stale).Should().BeNull();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
