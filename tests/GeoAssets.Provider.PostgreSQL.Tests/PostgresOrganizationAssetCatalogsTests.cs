using System.Security.Cryptography;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresOrganizationAssetCatalogs"/> and <see cref="OrganizationAssetCatalog"/>
/// (XD01-338) against the collection's migrated PostGIS instance: an organization reads its own
/// features plus the shared (organization-less) ones and never another organization's, through
/// every read the catalog serves; each organization's cache is its own; shared features are cached
/// once; writes land where the feature lives. Each test uses its own organizations, ids, asset type
/// and map area, so nothing is truncated.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresOrganizationAssetCatalogsTests(PostgresContainerFixture fixture)
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record World(
        Guid A, Guid B, string AssetType, string Token, double Lon, double Lat, string OwnA, string OwnB, string Shared);

    private sealed class Catalogs(PostgresOrganizationAssetCatalogs catalogs, ValidatingAssetProvider shared) : IAsyncDisposable
    {
        public PostgresOrganizationAssetCatalogs Sut { get; } = catalogs;

        public IAssetProvider For(Guid organizationId) => Sut.For(organizationId);

        public async ValueTask DisposeAsync()
        {
            await Sut.DisposeAsync();
            await shared.DisposeAsync();
        }
    }

    /// <summary>A fresh feature id within <c>geo_entity</c>'s 36 characters.</summary>
    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    private static GeoFeature Feature(
        string id, Guid organizationId, World world, double offset, params string[] edgesTo) => new()
    {
        Id = id,
        Geometry = new GeoPoint(world.Lon + offset, world.Lat + offset),
        Topology = [.. edgesTo.Select(target => new TopoEdge { TargetId = target, Kind = "feeds" })],
        Properties = new GeoFeatureProperties
        {
            Name = $"{world.Token} {id}",
            AssetTypeId = world.AssetType,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{id}" },
        },
    };

    /// <summary>
    /// Organizations A and B, one feature each and one shared feature, all within a small area of
    /// their own; A's feature has topology edges to the shared feature and to B's.
    /// </summary>
    private async Task<World> SeedWorldAsync()
    {
        var world = new World(
            A: Guid.NewGuid(),
            B: Guid.NewGuid(),
            AssetType: $"t-{Guid.NewGuid():N}",
            Token: $"tok{Guid.NewGuid():N}",
            Lon: RandomNumberGenerator.GetInt32(-170, 170) + 0.5,
            Lat: RandomNumberGenerator.GetInt32(-80, 80) + 0.5,
            OwnA: NewId("own-a"),
            OwnB: NewId("own-b"),
            Shared: NewId("shared"));

        await SeedAsync(
            Feature(world.OwnA, world.A, world, 0.001, world.Shared, world.OwnB),
            Feature(world.OwnB, world.B, world, 0.002),
            Feature(world.Shared, Guid.Empty, world, 0.003));
        return world;
    }

    private async Task SeedAsync(params GeoFeature[] features)
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddRange(features);
    }

    /// <summary>The host's arrangement: the warmed tenant-less provider as the shared catalog, after seeding.</summary>
    private async Task<Catalogs> NewCatalogsAsync()
    {
        var factory = new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System);
        var shared = new ValidatingAssetProvider(await factory.CreateAsync(fixture.ConnectionString));
        return new Catalogs(
            new PostgresOrganizationAssetCatalogs(fixture.CreateOptions(), shared, NullLoggerFactory.Instance, TimeProvider.System),
            shared);
    }

    private async Task<GeoEntityRowSnapshot?> RowAsync(string id)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntities.IgnoreQueryFilters()
            .Where(row => row.Id == id)
            .Select(row => new GeoEntityRowSnapshot(row.Name, row.OrganizationId))
            .SingleOrDefaultAsync();
    }

    private sealed record GeoEntityRowSnapshot(string Name, Guid OrganizationId);

    private async Task<List<string>> AttributesAsync(string id)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntityAttributes
            .Where(a => a.EntityId == id)
            .OrderBy(a => a.Key)
            .Select(a => a.Key + "=" + a.Value)
            .ToListAsync();
    }

    /// <summary>A tenant-less provider, standing for a shared catalog nobody reads in the test.</summary>
    private PostgresAssetProvider Unused()
    {
        var options = fixture.CreateOptions();
        return new PostgresAssetProvider(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }

    private static GeoPolygon Around(World world) => new(
    [
        (world.Lon - 0.1, world.Lat - 0.1), (world.Lon + 0.1, world.Lat - 0.1),
        (world.Lon + 0.1, world.Lat + 0.1), (world.Lon - 0.1, world.Lat + 0.1),
        (world.Lon - 0.1, world.Lat - 0.1),
    ]);

    private static IEnumerable<string> Ids(IEnumerable<GeoFeature> features) => features.Select(f => f.Id);

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryRead_OfAnOrganization_ReturnsItsFeaturesAndTheSharedOnes_NeverAnotherOrganizations()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        foreach (var (organization, own, foreign) in new[] { (world.A, world.OwnA, world.OwnB), (world.B, world.OwnB, world.OwnA) })
        {
            await catalogs.Sut.WarmAsync(organization, CancellationToken.None);
            var view = catalogs.For(organization);
            string[] expected = [own, world.Shared];

            view.GetById(own).Should().NotBeNull();
            view.GetById(world.Shared).Should().NotBeNull();
            view.GetById(foreign).Should().BeNull("another organization's feature is not in this view");

            Ids(view.GetAll()).Should().Contain(expected).And.NotContain(foreign);
            Ids(view.GetByAssetType(world.AssetType)).Should().BeEquivalentTo(expected);
            Ids(view.Search(world.Token)).Should().BeEquivalentTo(expected);
            Ids(view.GetWithin(Around(world))).Should().BeEquivalentTo(expected);
            Ids(view.GetIntersecting(Around(world))).Should().BeEquivalentTo(expected);
            Ids(view.GetNearby(new GeoPoint(world.Lon, world.Lat), 0.05)).Should().BeEquivalentTo(expected);

            var inBounds = await view.GetInBoundsAsync(world.Lon - 0.1, world.Lat - 0.1, world.Lon + 0.1, world.Lat + 0.1);
            Ids(inBounds).Should().BeEquivalentTo(expected);

            var json = await view.GetInBoundsJsonAsync(world.Lon - 0.1, world.Lat - 0.1, world.Lon + 0.1, world.Lat + 0.1);
            json.Select(e => e.GetProperty("id").GetString()).Should().BeEquivalentTo(expected);

            var page = await view.GetPageAsync(new AssetQuery { AssetTypeId = world.AssetType, Take = 1, SortBy = "name" });
            page.TotalCount.Should().Be(2, "the page counts the organization's and the shared features, in SQL");
            page.Items.Should().ContainSingle();
            var secondPage = await view.GetPageAsync(new AssetQuery { AssetTypeId = world.AssetType, Skip = 1, Take = 1, SortBy = "name" });
            Ids([.. page.Items, .. secondPage.Items]).Should().BeEquivalentTo(expected);

            var foreignPage = await view.GetPageAsync(new AssetQuery { FeatureIds = [foreign] });
            foreignPage.TotalCount.Should().Be(0);
            var searched = await view.GetPageAsync(new AssetQuery { SearchText = world.Token });
            Ids(searched.Items).Should().BeEquivalentTo(expected);
            var byAttribute = await view.GetPageAsync(new AssetQuery
            {
                AttributeFilters = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{foreign}" },
            });
            byAttribute.TotalCount.Should().Be(0);
        }

        Ids(catalogs.For(world.A).GetNeighbors(world.OwnA)).Should().Equal([world.Shared], "B's feature is not in A's graph");
    }

    [Fact]
    public async Task TheViewWithoutAnOrganization_IsTheSharedCatalogAlone_AsBefore()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        await catalogs.Sut.WarmAsync(Guid.Empty, CancellationToken.None);
        var view = catalogs.For(Guid.Empty);

        view.Should().BeOfType<OrganizationAssetCatalog>().Which.OrganizationId.Should().Be(Guid.Empty);
        view.GetById(world.Shared).Should().NotBeNull();
        view.GetById(world.OwnA).Should().BeNull();
        view.GetById(world.OwnB).Should().BeNull();
        Ids(view.GetByAssetType(world.AssetType)).Should().Equal(world.Shared);
        var inBounds = await view.GetInBoundsAsync(world.Lon - 0.1, world.Lat - 0.1, world.Lon + 0.1, world.Lat + 0.1);
        Ids(inBounds).Should().Equal(world.Shared);
        (await view.GetPageAsync(new AssetQuery { AssetTypeId = world.AssetType })).TotalCount.Should().Be(1);
    }

    // ── Caches ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EachOrganization_HasACatalogOfItsOwn_AndAFeatureWrittenForOneIsNeverServedToAnother()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        await catalogs.Sut.WarmAsync(world.A, CancellationToken.None);
        await catalogs.Sut.WarmAsync(world.B, CancellationToken.None);

        catalogs.For(world.A).Should().BeSameAs(catalogs.For(world.A), "an organization's catalog is built once");
        catalogs.For(world.A).Should().NotBeSameAs(catalogs.For(world.B));

        var added = NewId("added-a");
        catalogs.For(world.A).Add(Feature(added, world.A, world, 0.004));

        catalogs.For(world.A).GetById(added).Should().NotBeNull();
        catalogs.For(world.B).GetById(added).Should().BeNull("A's cache never serves B");
        catalogs.For(Guid.Empty).GetById(added).Should().BeNull();
        Ids(catalogs.For(world.B).GetByAssetType(world.AssetType)).Should().BeEquivalentTo([world.OwnB, world.Shared]);
        (await RowAsync(added))!.OrganizationId.Should().Be(world.A);
    }

    [Fact]
    public async Task AWriteToASharedFeature_GoesToTheSharedCatalog_AndEveryOrganizationSeesIt()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        await catalogs.Sut.WarmAsync(world.A, CancellationToken.None);
        await catalogs.Sut.WarmAsync(world.B, CancellationToken.None);

        var renamed = catalogs.For(world.A).GetById(world.Shared)!;
        renamed.Properties.Name = $"{world.Token} renamed";
        renamed.Properties.OrganizationId = world.A;
        catalogs.For(world.A).Update(renamed);

        catalogs.For(world.B).GetById(world.Shared)!.Properties.Name.Should().Be($"{world.Token} renamed");
        catalogs.For(Guid.Empty).GetById(world.Shared)!.Properties.Name.Should().Be($"{world.Token} renamed");
        (await RowAsync(world.Shared))!.OrganizationId.Should().Be(Guid.Empty, "an update never takes a shared feature into an organization (XD01-340)");

        var newOne = NewId("new-stated-shared");
        catalogs.For(world.A).Add(Feature(newOne, Guid.Empty, world, 0.005));
        (await RowAsync(newOne))!.OrganizationId.Should().Be(world.A, "a new feature is the organization's, whatever it states (XD01-340)");
        catalogs.For(world.B).GetById(newOne).Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ReloadsAnOrganizationsCatalog_AfterAWriteMadeOutsideIt()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        await catalogs.Sut.WarmAsync(world.A, CancellationToken.None);
        var view = catalogs.For(world.A);
        view.GetById(world.OwnA)!.Properties.Name.Should().Be($"{world.Token} {world.OwnA}");

        await using (var db = new GeoAssetsDbContext(fixture.CreateOptions()))
        {
            await db.GeoEntities.IgnoreQueryFilters()
                .Where(row => row.Id == world.OwnA)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Name, "changed elsewhere"));
        }

        view.GetById(world.OwnA)!.Properties.Name.Should().NotBe("changed elsewhere", "the cache is not reloaded by itself");

        await catalogs.Sut.RefreshAsync(world.A, CancellationToken.None);

        view.GetById(world.OwnA)!.Properties.Name.Should().Be("changed elsewhere");
        view.GetById(world.OwnB).Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ForAnOrganizationThisProcessHasNotLoaded_OrNoOrganization_DoesNothing()
    {
        await using var catalogs = await NewCatalogsAsync();

        await catalogs.Sut.RefreshAsync(Guid.NewGuid(), CancellationToken.None);
        await catalogs.Sut.RefreshAsync(Guid.Empty, CancellationToken.None);
        await catalogs.Sut.WarmAsync(Guid.Empty, CancellationToken.None);
    }

    [Fact]
    public async Task WarmAsync_HonoursTheCallersCancellation()
    {
        await using var catalogs = await NewCatalogsAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var warm = () => catalogs.Sut.WarmAsync(Guid.NewGuid(), cancelled.Token);

        await warm.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnOrganizationsCatalogNotYetWarmed_LoadsOnFirstUse()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        catalogs.For(world.A).GetById(world.OwnA).Should().NotBeNull();
        catalogs.For(world.A).GetById(world.OwnB).Should().BeNull();
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesTheOrganizationsOwnFeature_AndDoesNothingToAnotherOrganizations()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);

        view.Delete(world.OwnA);
        view.Delete(world.OwnB);

        (await RowAsync(world.OwnA)).Should().BeNull();
        view.GetById(world.OwnA).Should().BeNull();
        (await RowAsync(world.OwnB)).Should().NotBeNull("A cannot see, so cannot delete, B's feature");
    }

    [Fact]
    public async Task Delete_OfASharedFeature_RemovesItForEveryOrganization()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        catalogs.For(world.A).Delete(world.Shared);

        (await RowAsync(world.Shared)).Should().BeNull();
        catalogs.For(world.B).GetById(world.Shared).Should().BeNull();
    }

    [Fact]
    public async Task Clear_RemovesTheOrganizationsOwnFeaturesOnly()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        catalogs.For(world.A).Clear();

        (await RowAsync(world.OwnA)).Should().BeNull();
        (await RowAsync(world.OwnB)).Should().NotBeNull();
        (await RowAsync(world.Shared)).Should().NotBeNull();
    }

    [Fact]
    public async Task AddRange_StampsNewFeaturesWithTheOrganization_WhateverOrganizationTheyState()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var ownAdded = NewId("range-a");
        var statedShared = NewId("range-s");
        var statedOther = NewId("range-b");

        catalogs.For(world.A).AddRange(
        [
            Feature(ownAdded, world.A, world, 0.006),
            Feature(statedShared, Guid.Empty, world, 0.007),
            Feature(statedOther, world.B, world, 0.0075),
        ]);

        (await RowAsync(ownAdded))!.OrganizationId.Should().Be(world.A);
        (await RowAsync(statedShared))!.OrganizationId.Should().Be(world.A);
        (await RowAsync(statedOther))!.OrganizationId.Should().Be(world.A);
        Ids(catalogs.For(world.A).GetByAssetType(world.AssetType))
            .Should().BeEquivalentTo([world.OwnA, ownAdded, statedShared, statedOther, world.Shared]);
        Ids(catalogs.For(world.B).GetByAssetType(world.AssetType)).Should().BeEquivalentTo([world.OwnB, world.Shared]);
    }

    // ── Writes naming another organization's feature (XD01-340) ───────────────

    [Fact]
    public async Task Update_OfAnotherOrganizationsFeature_IsRefused_WhateverOrganizationItStates_AndLeavesItsRowUntouched()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);

        foreach (var stated in new[] { world.A, world.B, Guid.Empty })
        {
            var hijack = Feature(world.OwnB, stated, world, 0.05);
            hijack.Properties.Name = "hijacked";
            hijack.Properties.CustomAttributes["plate"] = "stolen";

            var update = () => view.Update(hijack);

            update.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal(world.OwnB);
        }

        (await RowAsync(world.OwnB)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} {world.OwnB}", world.B));
        (await AttributesAsync(world.OwnB)).Should().Equal($"plate=P-{world.OwnB}");
        catalogs.For(world.B).GetById(world.OwnB)!.Properties.Name.Should().Be($"{world.Token} {world.OwnB}");
    }

    [Fact]
    public async Task AddRange_NamingAnotherOrganizationsFeature_IsRefusedWhole_AndChangesNothing()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var added = NewId("range-new");
        var hijack = Feature(world.OwnB, world.A, world, 0.05);
        hijack.Properties.Name = "hijacked";

        var addRange = () => catalogs.For(world.A).AddRange([Feature(added, world.A, world, 0.011), hijack]);

        addRange.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal(world.OwnB);
        (await RowAsync(added)).Should().BeNull();
        (await RowAsync(world.OwnB)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} {world.OwnB}", world.B));
        (await AttributesAsync(world.OwnB)).Should().Equal(new[] { $"plate=P-{world.OwnB}" }, "the upsert's attribute reset must not reach the foreign row");
    }

    [Fact]
    public async Task LoadAll_StampsTheOrganization_AndNamingAnotherOrganizationsFeature_IsRefusedWithNothingRemoved()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);

        var refused = () => view.LoadAll([Feature(NewId("load-x"), world.A, world, 0.012), Feature(world.OwnB, world.A, world, 0.05)]);

        refused.Should().Throw<ForeignGeoFeatureWriteException>();
        (await RowAsync(world.OwnA)).Should().NotBeNull("a refused load removes nothing");
        (await RowAsync(world.OwnB))!.OrganizationId.Should().Be(world.B);

        var loaded = NewId("load-a");
        view.LoadAll([Feature(loaded, world.B, world, 0.013)]);

        (await RowAsync(loaded))!.OrganizationId.Should().Be(world.A);
        (await RowAsync(world.OwnA)).Should().BeNull("a load replaces the organization's own features");
        (await RowAsync(world.OwnB)).Should().NotBeNull();
        (await RowAsync(world.Shared)).Should().NotBeNull();
    }

    [Fact]
    public async Task Update_OfTheOrganizationsOwnFeature_StillWorks_AndKeepsItTheOrganizations()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);

        var renamed = Feature(world.OwnA, world.B, world, 0.001);
        renamed.Properties.Name = $"{world.Token} renamed";
        view.Update(renamed);

        (await RowAsync(world.OwnA)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} renamed", world.A));
        view.GetById(world.OwnA)!.Properties.Name.Should().Be($"{world.Token} renamed");
        catalogs.For(world.B).GetById(world.OwnA).Should().BeNull();
    }

    [Fact]
    public async Task RepeatedUpdates_OfTheSameOwnAndSharedFeatures_AllSucceed_AndTheLatestValuesAreStored()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();

        foreach (var (organization, round) in new[] { (world.A, 1), (world.A, 2), (world.B, 3) })
        {
            var view = catalogs.For(organization);
            foreach (var id in organization == world.A ? new[] { world.OwnA, world.Shared } : [world.Shared])
            {
                var renamed = view.GetById(id)!;
                renamed.Properties.Name = $"{world.Token} round {round}";
                renamed.Properties.CustomAttributes["plate"] = $"P-{round}";
                view.Update(renamed);
            }
        }

        (await RowAsync(world.OwnA)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} round 2", world.A));
        (await AttributesAsync(world.OwnA)).Should().Equal("plate=P-2");
        (await RowAsync(world.Shared)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} round 3", Guid.Empty));
        (await AttributesAsync(world.Shared)).Should().Equal("plate=P-3");
        catalogs.For(world.A).GetById(world.Shared)!.Properties.Name.Should().Be($"{world.Token} round 3");
    }

    [Fact]
    public async Task AddRange_OfTheOrganizationsStoredFeatures_UpdatesThem_AndTheSharedOnesStayShared()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);

        for (var round = 1; round <= 2; round++)
        {
            var own = Feature(world.OwnA, world.A, world, 0.001);
            own.Properties.Name = $"{world.Token} own {round}";
            own.Properties.CustomAttributes["plate"] = $"P-own-{round}";
            var shared = Feature(world.Shared, world.A, world, 0.003);
            shared.Properties.Name = $"{world.Token} shared {round}";
            shared.Properties.CustomAttributes["plate"] = $"P-shared-{round}";
            view.AddRange([own, shared]);
        }

        (await RowAsync(world.OwnA)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} own 2", world.A));
        (await AttributesAsync(world.OwnA)).Should().Equal("plate=P-own-2");
        (await RowAsync(world.Shared)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} shared 2", Guid.Empty));
        (await AttributesAsync(world.Shared)).Should().Equal("plate=P-shared-2");
        view.GetById(world.OwnA)!.Properties.Name.Should().Be($"{world.Token} own 2");
    }

    [Fact]
    public async Task TheViewWithoutAnOrganization_KeepsTheStatedOrganization_ButCannotOverwriteAnOrganizationsFeature()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(Guid.Empty);

        var stated = NewId("no-org-b");
        view.Add(Feature(stated, world.B, world, 0.014));
        (await RowAsync(stated))!.OrganizationId.Should().Be(world.B, "an organization-less caller's writes are unchanged");

        var renamed = view.GetById(world.Shared)!;
        renamed.Properties.Name = $"{world.Token} renamed";
        view.Update(renamed);
        (await RowAsync(world.Shared)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} renamed", Guid.Empty));

        var hijack = Feature(world.OwnA, Guid.Empty, world, 0.05);
        hijack.Properties.Name = "hijacked";
        var update = () => view.Update(hijack);

        update.Should().Throw<ForeignGeoFeatureWriteException>();
        (await RowAsync(world.OwnA)).Should().Be(new GeoEntityRowSnapshot($"{world.Token} {world.OwnA}", world.A));
        (await AttributesAsync(world.OwnA)).Should().Equal($"plate=P-{world.OwnA}");
    }

    [Fact]
    public async Task Events_OfBothParts_ReachTheViewsSubscribers()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var view = catalogs.For(world.A);
        var added = new List<string>();
        void OnAdded(object? sender, GeoFeature feature) => added.Add(feature.Id);
        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => changes++;
        view.FeatureAdded += OnAdded;
        view.CollectionChanged += OnChanged;

        var updated = new List<string>();
        void OnUpdated(object? sender, GeoFeature feature) => updated.Add(feature.Id);
        view.FeatureUpdated += OnUpdated;

        var ownAdded = NewId("event-a");
        view.Add(Feature(ownAdded, world.A, world, 0.008));
        view.Update(view.GetById(world.Shared)!);
        view.FeatureAdded -= OnAdded;
        view.FeatureUpdated -= OnUpdated;
        view.CollectionChanged -= OnChanged;
        view.Add(Feature(NewId("event-late"), world.A, world, 0.010));

        added.Should().Equal(ownAdded);
        updated.Should().Equal(world.Shared);
        changes.Should().Be(2);
    }

    // ── Asset types and layers ────────────────────────────────────────────────

    [Fact]
    public async Task AssetTypes_OfAnOrganization_AreItsOwnAndTheShared_AndAnotherOrganizationsCannotBeDeleted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ownType = new AssetType { Id = Guid.NewGuid(), Name = "own", OrganizationId = a };
        var foreignType = new AssetType { Id = Guid.NewGuid(), Name = "foreign", OrganizationId = b };
        var sharedType = new AssetType { Id = Guid.NewGuid(), Name = "shared" };
        await using var catalogs = await NewCatalogsAsync();
        catalogs.For(Guid.Empty).AddAssetType(ownType);
        catalogs.For(Guid.Empty).AddAssetType(foreignType);
        catalogs.For(Guid.Empty).AddAssetType(sharedType);

        var ofA = catalogs.For(a).GetAssetTypes().Select(t => t.Id).ToList();
        ofA.Should().Contain([ownType.Id, sharedType.Id]).And.NotContain(foreignType.Id);
        catalogs.For(Guid.Empty).GetAssetTypes().Select(t => t.Id).Should().Contain([ownType.Id, foreignType.Id, sharedType.Id],
            "the view without an organization lists every asset type, as before");

        catalogs.For(a).DeleteAssetType(foreignType.Id);
        catalogs.For(b).GetAssetTypes().Should().Contain(t => t.Id == foreignType.Id, "A cannot delete B's asset type");

        catalogs.For(a).DeleteAssetType(ownType.Id);
        catalogs.For(Guid.Empty).GetAssetTypes().Should().NotContain(t => t.Id == ownType.Id);

        catalogs.For(b).DeleteAssetType(foreignType.Id);
        catalogs.For(Guid.Empty).DeleteAssetType(sharedType.Id);
    }

    [Fact]
    public async Task LayersAndLayerRules_AreTheSharedCatalogs_ForEveryOrganization()
    {
        await using var catalogs = await NewCatalogsAsync();
        var a = Guid.NewGuid();
        var layer = new Layer { Id = Guid.NewGuid(), Name = $"layer-{Guid.NewGuid():N}", GeometryType = GeometryType.Point, Color = "#123456" };
        var assetTypeId = catalogs.For(Guid.Empty).GetAssetTypes()[0].Id;
        var rule = new LayerRule { Id = Guid.NewGuid(), AssetTypeId = assetTypeId, LayerId = layer.Id, Priority = 99 };

        catalogs.For(a).AddLayer(layer);
        catalogs.For(a).AddLayerRule(rule);

        catalogs.For(Guid.NewGuid()).GetLayers().Should().Contain(l => l.Id == layer.Id);
        catalogs.For(Guid.Empty).GetLayerRules(assetTypeId).Should().Contain(r => r.Id == rule.Id);

        catalogs.For(a).DeleteLayerRule(rule.Id);
        catalogs.For(a).DeleteLayer(layer.Id);
        catalogs.For(Guid.Empty).GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }

    // ── The refreshing applier ────────────────────────────────────────────────

    [Fact]
    public async Task TheRefreshingApplier_LeavesTheOrganizationsCatalogShowingTheAppliedBatch()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        await catalogs.Sut.WarmAsync(world.A, CancellationToken.None);
        var applier = new OrganizationCatalogRefreshingApplier(
            new PostgresAssetMutationBatchApplier(fixture.CreateOptions(), NullLoggerFactory.Instance, TimeProvider.System),
            catalogs.Sut);
        var batch = AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "default",
            organizationId: world.A,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations:
            [
                new AssetFeatureMutationItem(
                    FeatureId: world.OwnA,
                    MutationKind: AssetMutationKind.UpdateAttributes,
                    Name: string.Empty,
                    AssetTypeId: world.AssetType,
                    Description: string.Empty,
                    LayerId: string.Empty,
                    Geometry: null,
                    TopologyEdges: [],
                    CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["occupancyClass"] = "High" }),
            ],
            timeProvider: TimeProvider.System,
            batchId: Guid.NewGuid(),
            sourceOrderId: "order-1",
            correlationId: "order-1");

        var applied = await applier.ApplyAsync(batch, CancellationToken.None);

        applied.BatchId.Should().Be(batch.BatchId);
        catalogs.For(world.A).GetById(world.OwnA)!.Properties.CustomAttributes.Should().Contain("occupancyClass", "High");
    }

    [Fact]
    public async Task TheRefreshingApplier_RejectsMissingCollaborators()
    {
        await using var shared = Unused();
        await using var catalogs = new PostgresOrganizationAssetCatalogs(
            fixture.CreateOptions(), shared, NullLoggerFactory.Instance, TimeProvider.System);
        var applier = new PostgresAssetMutationBatchApplier(fixture.CreateOptions(), NullLoggerFactory.Instance, TimeProvider.System);

        ((Action)(() => _ = new OrganizationCatalogRefreshingApplier(null!, catalogs))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrganizationCatalogRefreshingApplier(applier, null!))).Should().Throw<ArgumentNullException>();
        var apply = () => new OrganizationCatalogRefreshingApplier(applier, catalogs).ApplyAsync(null!, CancellationToken.None);
        await apply.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Construction and registration ─────────────────────────────────────────

    [Fact]
    public async Task TheCatalogs_RejectMissingCollaborators()
    {
        var options = fixture.CreateOptions();
        await using var shared = Unused();

        ((Action)(() => _ = new PostgresOrganizationAssetCatalogs(null!, shared, NullLoggerFactory.Instance, TimeProvider.System))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new PostgresOrganizationAssetCatalogs(options, null!, NullLoggerFactory.Instance, TimeProvider.System))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new PostgresOrganizationAssetCatalogs(options, shared, null!, TimeProvider.System))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new PostgresOrganizationAssetCatalogs(options, shared, NullLoggerFactory.Instance, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddPostgresOrganizationAssetCatalogs_MakesIAssetProviderTheRequestOrganizationsView_AndKeepsTheSharedCatalogASingleton()
    {
        var world = await SeedWorldAsync();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(fixture.ConnectionString);
        services.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString);
        var tenant = new SwitchableTenant();
        services.AddScoped<ITenantContext>(_ => tenant);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await provider.WarmPostgresAssetProviderAsync();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService)
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        provider.GetRequiredKeyedService<IAssetProvider>(PostgresOrganizationCatalogServiceExtensions.SharedCatalogKey)
            .Should().BeOfType<ValidatingAssetProvider>();

        tenant.OrganizationId = world.A;
        await using (var scope = provider.CreateAsyncScope())
        {
            var view = scope.ServiceProvider.GetRequiredService<IAssetProvider>();
            view.Should().BeOfType<OrganizationAssetCatalog>().Which.OrganizationId.Should().Be(world.A);
            view.GetById(world.OwnA).Should().NotBeNull();
            view.GetById(world.OwnB).Should().BeNull();
        }

        tenant.OrganizationId = Guid.Empty;
        await using (var scope = provider.CreateAsyncScope())
        {
            var view = scope.ServiceProvider.GetRequiredService<IAssetProvider>();
            view.GetById(world.Shared).Should().NotBeNull();
            view.GetById(world.OwnA).Should().BeNull();
        }

        tenant.OrganizationId = world.A;
        await using (var scope = provider.CreateAsyncScope())
            scope.ServiceProvider.GetRequiredService<IAssetProvider>().GetById(world.OwnA).Should().NotBeNull("ending a scope disposes nothing the catalogs own");
    }

    [Fact]
    public async Task AddPostgresOrganizationAssetCatalogs_WithoutATenantContext_ServesTheSharedCatalog()
    {
        var world = await SeedWorldAsync();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAssetProvider>(_ => Unused());
        services.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var view = scope.ServiceProvider.GetRequiredService<IAssetProvider>();

        view.Should().BeOfType<OrganizationAssetCatalog>().Which.OrganizationId.Should().Be(Guid.Empty);
        view.GetById(world.Shared).Should().NotBeNull();
        view.GetById(world.OwnA).Should().BeNull();
    }

    [Fact]
    public void AddPostgresOrganizationAssetCatalogs_NeedsTheSharedCatalogAsASingleton_AndItsArguments()
    {
        var none = new ServiceCollection();
        ((Action)(() => none.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString)))
            .Should().Throw<InvalidOperationException>();

        var scoped = new ServiceCollection();
        scoped.AddScoped<IAssetProvider, PostgresAssetProvider>();
        ((Action)(() => scoped.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString)))
            .Should().Throw<InvalidOperationException>();

        ((Action)(() => ((IServiceCollection)null!).AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddPostgresOrganizationAssetCatalogs(" ")))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddPostgresOrganizationAssetCatalogs_ResolvesASharedCatalogRegisteredAsAnInstanceOrAType()
    {
        await using var shared = Unused();
        var instance = new ServiceCollection();
        instance.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        instance.AddSingleton<IAssetProvider>(shared);
        instance.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString);
        await using (var provider = instance.BuildServiceProvider())
            provider.GetRequiredKeyedService<IAssetProvider>(PostgresOrganizationCatalogServiceExtensions.SharedCatalogKey).Should().BeSameAs(shared);

        var options = fixture.CreateOptions();
        var type = new ServiceCollection();
        type.AddLogging();
        type.AddSingleton(TimeProvider.System);
        type.AddSingleton(options);
        type.AddTransient(_ => new GeoAssetsDbContext(options));
        type.AddSingleton<IAssetProvider, PostgresAssetProvider>();
        type.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString);
        await using (var provider = type.BuildServiceProvider())
            provider.GetRequiredKeyedService<IAssetProvider>(PostgresOrganizationCatalogServiceExtensions.SharedCatalogKey).Should().BeOfType<PostgresAssetProvider>();
    }

    [Fact]
    public async Task AddPostgresOrganizationAssetCatalogs_RegistersTheCatalogsAsTheIOrganizationCatalogFactorySingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(fixture.ConnectionString);
        services.AddPostgresOrganizationAssetCatalogs(fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.WarmPostgresAssetProviderAsync();

        var factory = provider.GetRequiredService<IOrganizationCatalogFactory>();

        factory.Should().BeSameAs(provider.GetRequiredService<PostgresOrganizationAssetCatalogs>());
        factory.Should().BeSameAs(provider.GetRequiredService<IOrganizationCatalogFactory>());
    }

    [Fact]
    public async Task GetForOrganization_IsFor()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var factory = catalogs.Sut;

        factory.GetForOrganization(world.A).Should().BeSameAs(catalogs.For(world.A));
        factory.GetForOrganization(Guid.Empty).Should().BeSameAs(catalogs.For(Guid.Empty));
    }

    [Fact]
    public async Task GetForOrganizationAsync_ReturnsTheWarmedCatalog_EqualToFor()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var factory = catalogs.Sut;

        var view = await factory.GetForOrganizationAsync(world.A, CancellationToken.None);

        view.Should().BeSameAs(catalogs.For(world.A));
        view.GetById(world.OwnA).Should().NotBeNull();
        view.GetById(world.Shared).Should().NotBeNull();
        view.GetById(world.OwnB).Should().BeNull();
    }

    [Fact]
    public async Task GetForOrganizationAsync_EmptyOrganization_ReturnsTheSharedOnlyCatalog()
    {
        var world = await SeedWorldAsync();
        await using var catalogs = await NewCatalogsAsync();
        var factory = catalogs.Sut;

        var view = await factory.GetForOrganizationAsync(Guid.Empty, CancellationToken.None);

        view.Should().BeSameAs(catalogs.For(Guid.Empty));
        view.GetById(world.Shared).Should().NotBeNull();
        view.GetById(world.OwnA).Should().BeNull();
    }

    private sealed class SwitchableTenant : ITenantContext
    {
        public Guid OrganizationId { get; set; }

        public Guid CurrentOrganizationId => OrganizationId;
    }
}
