using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresAssetSpatialQuery"/> against a real PostGIS instance (XD01-307):
/// the pushed-down <c>ST_Covers</c>/attribute lookups, the bounded-result contract, tenant
/// isolation (application filter, explicit predicate and Row-Level Security), and parity with the
/// default in-memory <see cref="AssetProviderSpatialQuery"/> on the same seeded data.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetSpatialQueryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string AppRole = "geoassets_app";

    // Same value GeoEntityForceRowLevelSecurityTests sets — both classes share the container's role.
    private const string AppRolePassword = "geoassets_app_test_password";

    private static readonly Guid OrgA = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000307");
    private static readonly Guid OrgB = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000307");

    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly AssetSpatialFilter Unfiltered = new(null, null, int.MaxValue);

    // 10° × 10° square with its south-west corner on the origin.
    private static readonly GeoPolygon Area = new([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]);

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync()
    {
        // The app-role test opens pooled connections as geoassets_app; drop them so no later test
        // class is handed one — same precaution as GeoEntityForceRowLevelSecurityTests.
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private static GeoFeature Feature(
        string id, GeoGeometry? geometry = null, string assetTypeId = "type-pole", Guid? organizationId = null,
        params (string Key, string Value)[] attributes) =>
        new()
        {
            Id = id,
            Geometry = geometry,
            Properties = new GeoFeatureProperties
            {
                Name = id,
                AssetTypeId = assetTypeId,
                OrganizationId = organizationId ?? Guid.Empty,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime,
                CustomAttributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
            },
        };

    /// <summary>Seeds through the real provider, so <c>geo_entity_attribute</c> is populated exactly as in production.</summary>
    private async Task SeedAsync(params GeoFeature[] features)
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddRange(features);
    }

    private PostgresAssetSpatialQuery NewSut(Guid? tenant = null) =>
        new(fixture.CreateOptions(), tenant is { } organizationId ? new FakeTenantContext(organizationId) : null);

    private static async Task<IReadOnlyList<string>> IdsAsync(Task<IReadOnlyList<GeoFeature>> query) =>
        [.. (await query).Select(f => f.Id)];

    // ── Covered-by ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FindCoveredByAsync_IncludesFeaturesExactlyOnTheBoundary_AndExcludesNullGeometry()
    {
        await SeedAsync(
            Feature("inside", new GeoPoint(5, 5)),
            Feature("on-edge", new GeoPoint(10, 5)),
            Feature("on-vertex", new GeoPoint(0, 0)),
            Feature("outside", new GeoPoint(10.0001, 5)),
            Feature("no-geometry"),
            Feature("line-along-edge", new GeoLineString([(0, 0), (10, 0)])),
            Feature("line-crossing", new GeoLineString([(5, 5), (15, 5)])));

        var ids = await IdsAsync(NewSut().FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));

        ids.Should().Equal("inside", "line-along-edge", "on-edge", "on-vertex");
    }

    [Fact]
    public async Task FindCoveredByAsync_MapsTheWholeFeature()
    {
        await SeedAsync(Feature("pole-1", new GeoPoint(5, 6), "type-pole", null, ("plate", "P-100")));

        var result = await NewSut().FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        var feature = result.Should().ContainSingle().Subject;
        feature.Geometry.Should().BeOfType<GeoPoint>().Which.Coordinates.Should().Equal(5, 6);
        feature.Properties.AssetTypeId.Should().Be("type-pole");
        feature.Properties.CustomAttributes.Should().Contain("plate", "P-100");
    }

    [Fact]
    public async Task FindCoveredByAsync_NoMatch_ReturnsEmptyListNotNull()
    {
        await SeedAsync(Feature("far", new GeoPoint(50, 50)));

        var result = await NewSut().FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task FindCoveredByAsync_AssetTypeIds_FilterInSql_NullOrEmptyDoesNot()
    {
        await SeedAsync(
            Feature("pole", new GeoPoint(1, 1), "type-pole"),
            Feature("box", new GeoPoint(2, 2), "type-box"),
            Feature("duct", new GeoPoint(3, 3), "type-duct"));
        var sut = NewSut();

        var two = await IdsAsync(sut.FindCoveredByAsync(
            Area, new AssetSpatialFilter(["type-pole", "type-duct"], null, int.MaxValue), CancellationToken.None));
        var empty = await IdsAsync(sut.FindCoveredByAsync(
            Area, new AssetSpatialFilter([], null, int.MaxValue), CancellationToken.None));
        var none = await IdsAsync(sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));

        two.Should().Equal("duct", "pole");
        empty.Should().Equal("box", "duct", "pole");
        none.Should().Equal("box", "duct", "pole");
    }

    [Fact]
    public async Task FindCoveredByAsync_MaxResults_CutsAfterOrdinalIdOrdering_Deterministically()
    {
        // Seed order differs from id order, and "B" sorts before "a" only under ordinal (byte)
        // comparison — a linguistic database collation would put "a" first.
        await SeedAsync(
            Feature("c", new GeoPoint(1, 1)),
            Feature("a", new GeoPoint(2, 2)),
            Feature("B", new GeoPoint(3, 3)),
            Feature("b", new GeoPoint(4, 4)),
            Feature("_x", new GeoPoint(5, 5)));
        var sut = NewSut();

        var all = await IdsAsync(sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));
        var cut = await IdsAsync(sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, null, 2), CancellationToken.None));
        var cutAgain = await IdsAsync(sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, null, 2), CancellationToken.None));

        all.Should().Equal("B", "_x", "a", "b", "c");
        all.Should().BeInAscendingOrder(StringComparer.Ordinal);
        cut.Should().Equal("B", "_x");
        cutAgain.Should().Equal(cut);
    }

    [Fact]
    public async Task FindCoveredByAsync_PushesAreaTypeOrderAndLimitDownToPostgres_InOneParameterizedQuery()
    {
        await SeedAsync(Feature("a", new GeoPoint(1, 1)), Feature("b", new GeoPoint(2, 2)));
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        var sut = new PostgresAssetSpatialQuery(options, new FakeTenantContext(Guid.Empty));

        var result = await sut.FindCoveredByAsync(
            Area, new AssetSpatialFilter(["type-pole"], Guid.Empty, 1), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("a");
        var sql = interceptor.CommandTexts.Should().ContainSingle().Subject;
        sql.Should().Contain("ST_Covers").And.Contain("LIMIT").And.Contain("COLLATE \"C\"").And.Contain("\"OrganizationId\" = @");
        sql.Should().NotContain("type-pole", "values travel as parameters, never as SQL text");
    }

    // ── Attribute values ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindByAttributeValuesAsync_MatchesExactValuesOfThatKey_AndReturnsEveryFeatureSharingAValue()
    {
        await SeedAsync(
            Feature("a", attributes: ("plate", "100")),
            Feature("b", attributes: ("plate", "100")),
            Feature("c", attributes: ("plate", "ABC")),
            Feature("d", attributes: ("plate", "abc")),
            Feature("e", attributes: ("other", "100")),
            Feature("f", attributes: ("plate", "1000")),
            Feature("g"));

        var ids = await IdsAsync(NewSut().FindByAttributeValuesAsync(
            "plate", ["100", "abc", "not-found"], Unfiltered, CancellationToken.None));

        ids.Should().Equal("a", "b", "d");
    }

    [Fact]
    public async Task FindByAttributeValuesAsync_ReservedIdKey_MatchesFeatureIdNotACustomAttribute()
    {
        await SeedAsync(
            Feature("f-1"),
            Feature("f-2", attributes: ("id", "f-1")),
            Feature("f-3"));

        var ids = await IdsAsync(NewSut().FindByAttributeValuesAsync(
            AssetSpatialQueryKeys.FeatureId, ["f-1", "f-3", "missing"], Unfiltered, CancellationToken.None));

        ids.Should().Equal("f-1", "f-3");
    }

    [Fact]
    public async Task FindByAttributeValuesAsync_AppliesTypeFilterAndMaxResultsAfterOrdering()
    {
        await SeedAsync(
            Feature("z", assetTypeId: "type-pole", attributes: ("plate", "1")),
            Feature("y", assetTypeId: "type-pole", attributes: ("plate", "2")),
            Feature("x", assetTypeId: "type-box", attributes: ("plate", "3")),
            Feature("w", assetTypeId: "type-pole", attributes: ("plate", "3")));

        var ids = await IdsAsync(NewSut().FindByAttributeValuesAsync(
            "plate", ["1", "2", "3"], new AssetSpatialFilter(["type-pole"], null, 2), CancellationToken.None));

        ids.Should().Equal("w", "y");
    }

    [Fact]
    public async Task FindByAttributeValuesAsync_EmptyValues_ReturnsEmptyListWithoutQuerying()
    {
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;

        var result = await new PostgresAssetSpatialQuery(options)
            .FindByAttributeValuesAsync("plate", [], Unfiltered, CancellationToken.None);

        result.Should().NotBeNull().And.BeEmpty();
        interceptor.CommandTexts.Should().BeEmpty();
    }

    [Fact]
    public async Task FindByAttributeValuesAsync_PushesTheLookupDownToTheAttributeTable_WithValuesAsOneArrayParameter()
    {
        await SeedAsync(Feature("a", attributes: ("plate", "100")));
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;

        var result = await new PostgresAssetSpatialQuery(options).FindByAttributeValuesAsync(
            "plate", ["100", "'; DROP TABLE geo_entity; --"], Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("a");
        var sql = interceptor.CommandTexts.Should().ContainSingle().Subject;
        sql.Should().Contain("geo_entity_attribute").And.Contain("= ANY (@").And.Contain("LIMIT");
        sql.Should().NotContain("DROP TABLE").And.NotContain("'100'").And.NotContain("'plate'");
    }

    // ── Tenant isolation (finding B2) ────────────────────────────────────────

    private Task SeedTwoOrganizationsAsync() => SeedAsync(
        Feature("a-1", new GeoPoint(1, 1), organizationId: OrgA, attributes: ("plate", "100")),
        Feature("a-2", new GeoPoint(2, 2), organizationId: OrgA, attributes: ("plate", "200")),
        Feature("b-1", new GeoPoint(1, 1), organizationId: OrgB, attributes: ("plate", "100")),
        Feature("b-2", new GeoPoint(3, 3), organizationId: OrgB, attributes: ("plate", "300")),
        Feature("legacy", new GeoPoint(4, 4), attributes: ("plate", "100")));

    [Fact]
    public async Task TwoOrganizations_FilterForOrgA_NeverReturnsOrgBFeatures()
    {
        await SeedTwoOrganizationsAsync();
        var sut = NewSut(tenant: OrgA);
        var forA = new AssetSpatialFilter(null, OrgA, int.MaxValue);

        var covered = await sut.FindCoveredByAsync(Area, forA, CancellationToken.None);
        var byPlate = await sut.FindByAttributeValuesAsync("plate", ["100", "200", "300"], forA, CancellationToken.None);
        var byId = await sut.FindByAttributeValuesAsync(
            AssetSpatialQueryKeys.FeatureId, ["a-1", "b-1", "b-2", "legacy"], forA, CancellationToken.None);

        covered.Select(f => f.Id).Should().Equal("a-1", "a-2");
        byPlate.Select(f => f.Id).Should().Equal("a-1", "a-2");
        byId.Select(f => f.Id).Should().Equal("a-1");
        covered.Concat(byPlate).Concat(byId).Should().OnlyContain(f => f.Properties.OrganizationId == OrgA);
    }

    [Fact]
    public async Task TwoOrganizations_TenantAWithoutAnOrganizationFilter_StillSeesOnlyOrgA()
    {
        await SeedTwoOrganizationsAsync();
        var sut = NewSut(tenant: OrgA);

        var covered = await IdsAsync(sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));
        var byPlate = await IdsAsync(sut.FindByAttributeValuesAsync("plate", ["100", "300"], Unfiltered, CancellationToken.None));

        covered.Should().Equal("a-1", "a-2");
        byPlate.Should().Equal("a-1");
    }

    [Fact]
    public async Task TwoOrganizations_TenantAAskingForOrgB_GetsNothing()
    {
        await SeedTwoOrganizationsAsync();
        var sut = NewSut(tenant: OrgA);
        var forB = new AssetSpatialFilter(null, OrgB, int.MaxValue);

        var covered = await sut.FindCoveredByAsync(Area, forB, CancellationToken.None);
        var byPlate = await sut.FindByAttributeValuesAsync("plate", ["100", "300"], forB, CancellationToken.None);
        var byId = await sut.FindByAttributeValuesAsync(AssetSpatialQueryKeys.FeatureId, ["b-1"], forB, CancellationToken.None);

        covered.Should().BeEmpty();
        byPlate.Should().BeEmpty();
        byId.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoOrganizations_NoTenantContext_SeesOnlyTheUnassignedRows_NeverAnotherOrganizations()
    {
        await SeedTwoOrganizationsAsync();
        var sut = NewSut();

        var unfiltered = await IdsAsync(sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));
        var forA = await IdsAsync(sut.FindCoveredByAsync(
            Area, new AssetSpatialFilter(null, OrgA, int.MaxValue), CancellationToken.None));

        unfiltered.Should().Equal("legacy");
        forA.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoOrganizations_AsTheNonOwnerAppRole_RowLevelSecurityAdmitsOnlyTheStampedTenant()
    {
        await SeedTwoOrganizationsAsync();
        await using (var admin = new GeoAssetsDbContext(fixture.CreateOptions()))
            await admin.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD '{AppRolePassword}'");

        var appRoleConnectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = AppRole,
            Password = AppRolePassword,
        }.ConnectionString;
        var appRoleOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(appRoleConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        // Control: with nothing stamped, the policy hides every row from this role — and
        // IgnoreQueryFilters rules out the application-level filter as the reason.
        await using (var unstamped = new GeoAssetsDbContext(appRoleOptions))
            (await unstamped.GeoEntities.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var forA = await IdsAsync(new PostgresAssetSpatialQuery(appRoleOptions, new FakeTenantContext(OrgA))
            .FindCoveredByAsync(Area, Unfiltered, CancellationToken.None));
        var forB = await IdsAsync(new PostgresAssetSpatialQuery(appRoleOptions, new FakeTenantContext(OrgB))
            .FindByAttributeValuesAsync(AssetSpatialQueryKeys.FeatureId, ["a-1", "b-1", "b-2"], Unfiltered, CancellationToken.None));

        forA.Should().Equal("a-1", "a-2");
        forB.Should().Equal("b-1", "b-2");

        // The stamp was transaction-local: the pooled connection carries no organization over to
        // its next user (Postgres leaves a once-set custom setting blank, never the old value).
        await using var pooled = new NpgsqlConnection(appRoleConnectionString);
        await pooled.OpenAsync();
        await using var readSetting = pooled.CreateCommand();
        readSetting.CommandText = "SELECT coalesce(current_setting('app.current_organization_id', true), '')";
        (await readSetting.ExecuteScalarAsync()).Should().Be(string.Empty);
    }

    // ── Guards and cancellation ──────────────────────────────────────────────

    [Fact]
    public async Task Guards_MatchTheDefaultAdapters()
    {
        var sut = NewSut();
        var zero = new AssetSpatialFilter(null, null, 0);

        FluentActions.Invoking(() => new PostgresAssetSpatialQuery(null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindCoveredByAsync(null!, Unfiltered, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindCoveredByAsync(Area, null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindCoveredByAsync(Area, zero, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync(null!, ["1"], Unfiltered, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("plate", null!, Unfiltered, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("plate", ["1"], null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("plate", ["1"], zero, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("  ", ["1"], Unfiltered, CancellationToken.None))
            .Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceledException_WithoutQuerying()
    {
        await SeedAsync(Feature("a", new GeoPoint(1, 1), attributes: ("plate", "1")));
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        var sut = new PostgresAssetSpatialQuery(options);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Invoking(() => sut.FindCoveredByAsync(Area, Unfiltered, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("plate", ["1"], Unfiltered, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => sut.FindByAttributeValuesAsync("plate", [], Unfiltered, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        interceptor.CommandTexts.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationWhileTheQueryIsRunning_IsHonouredByPostgres()
    {
        await SeedAsync(Feature("a", new GeoPoint(1, 1)));
        using var cts = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(new CancelOnFirstReaderInterceptor(cts))
            .Options;
        var sut = new PostgresAssetSpatialQuery(options);

        await FluentActions.Invoking(() => sut.FindCoveredByAsync(Area, Unfiltered, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPostgresAssetSpatialQuery_NextToTheWarmedProvider_RegistersTheAdapterAsScoped_BoundToTheScopesTenant()
    {
        await SeedTwoOrganizationsAsync();

        // Mirrors GeoAssets.Server/Program.cs's own registration order.
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(fixture.ConnectionString);
        services.AddPostgresAssetSpatialQuery(fixture.ConnectionString);
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(OrgB));

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await provider.WarmPostgresAssetProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var sut = scope.ServiceProvider.GetRequiredService<IAssetSpatialQuery>();

        sut.Should().BeOfType<PostgresAssetSpatialQuery>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAssetSpatialQuery))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        (await IdsAsync(sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None))).Should().Equal("b-1", "b-2");
    }

    [Fact]
    public void AddPostgresAssetSpatialQuery_ReplacesAnEarlierRegistration_AndWorksWithoutATenantContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssetSpatialQuery>(_ => throw new InvalidOperationException("replaced"));

        services.AddPostgresAssetSpatialQuery(fixture.ConnectionString);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IAssetSpatialQuery>()
            .Should().ContainSingle().Which.Should().BeOfType<PostgresAssetSpatialQuery>();
    }

    [Fact]
    public async Task ExistingPostgresRegistrations_Alone_DoNotRegisterIAssetSpatialQuery()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);

        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(fixture.ConnectionString);
        services.AddGeoAssetsDbContextFactory(fixture.ConnectionString);

        services.Should().NotContain(d => d.ServiceType == typeof(IAssetSpatialQuery));
    }

    // ── Parity with the default adapter ──────────────────────────────────────

    public static TheoryData<int[]?, int> ParityFilters => new()
    {
        { null, int.MaxValue },
        { [0], int.MaxValue },
        { [1, 2], int.MaxValue },
        { null, 7 },
        { [0, 2], 3 },
        { [], 1 },
    };

    private static readonly string[] ParityTypeIds = ["type-pole", "type-box", "type-duct"];

    private static List<GeoFeature> BuildParityFeatures()
    {
        var features = new List<GeoFeature>();

        // A 13 × 13 lattice from -1 to 11 in steps of 1: rows/columns 0 and 10 sit exactly on the
        // area's boundary, -1 and 11 just outside it.
        for (var x = -1; x <= 11; x++)
        {
            for (var y = -1; y <= 11; y++)
            {
                var n = ((x + 1) * 13) + y + 1;
                var prefix = (n % 3) switch { 0 => "P", 1 => "p", _ => "_" };
                features.Add(Feature(
                    $"{prefix}-{n:D3}",
                    new GeoPoint(x, y),
                    ParityTypeIds[n % 3],
                    null,
                    ("plate", $"PL-{n % 40:D2}"), ("grid", $"{x}:{y}")));
            }
        }

        features.Add(Feature("line-inside", new GeoLineString([(1, 1), (9, 9)]), "type-duct", null, ("plate", "PL-01")));
        features.Add(Feature("line-on-boundary", new GeoLineString([(0, 10), (10, 10)]), "type-duct"));
        features.Add(Feature("line-crossing", new GeoLineString([(9, 9), (11, 11)]), "type-duct"));
        features.Add(Feature("polygon-inside", new GeoPolygon([(2, 2), (4, 2), (4, 4), (2, 4), (2, 2)]), "type-box"));
        features.Add(Feature("polygon-equal", new GeoPolygon([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]), "type-box"));
        features.Add(Feature("polygon-overlapping", new GeoPolygon([(8, 8), (12, 8), (12, 12), (8, 12), (8, 8)]), "type-box"));
        features.Add(Feature("no-geometry", null, "type-pole", null, ("plate", "PL-02")));
        return features;
    }

    [Theory]
    [MemberData(nameof(ParityFilters))]
    public async Task Parity_PostgresAdapterAndDefaultAdapter_ReturnTheSameIdsInTheSameOrder(int[]? typeIndexes, int maxResults)
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddRange(BuildParityFeatures());

        var inMemory = new AssetProviderSpatialQuery(provider);
        var postgres = NewSut();
        var typeIds = typeIndexes?.Select(i => ParityTypeIds[i]).ToArray();

        GeoPolygon[] areas =
        [
            Area,
            new([(2.5, 2.5), (7.5, 2.5), (7.5, 7.5), (2.5, 7.5), (2.5, 2.5)]),
            new([(0, 0), (10, 0), (5, 10), (0, 0)]),
            new([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)], [[(3, 3), (7, 3), (7, 7), (3, 7), (3, 3)]]),
            new([(40, 40), (41, 40), (41, 41), (40, 40)]),
        ];
        (string Key, string[] Values)[] lookups =
        [
            ("plate", ["PL-01", "PL-02", "PL-39", "pl-01", "missing"]),
            ("grid", ["0:0", "10:10", "11:11"]),
            ("unknown-key", ["PL-01"]),
            (AssetSpatialQueryKeys.FeatureId, ["P-003", "p-004", "_-005", "no-geometry", "missing"]),
        ];

        foreach (var organizationId in new Guid?[] { null, Guid.Empty, OrgA })
        {
            var filter = new AssetSpatialFilter(typeIds, organizationId, maxResults);

            foreach (var area in areas)
            {
                var expected = await IdsAsync(inMemory.FindCoveredByAsync(area, filter, CancellationToken.None));
                var actual = await IdsAsync(postgres.FindCoveredByAsync(area, filter, CancellationToken.None));

                actual.Should().Equal(expected, "covered-by parity for {0} with {1}", area.Coordinates.Length, filter);
            }

            foreach (var (key, values) in lookups)
            {
                var expected = await IdsAsync(inMemory.FindByAttributeValuesAsync(key, values, filter, CancellationToken.None));
                var actual = await IdsAsync(postgres.FindByAttributeValuesAsync(key, values, filter, CancellationToken.None));

                actual.Should().Equal(expected, "attribute parity for key '{0}' with {1}", key, filter);
            }
        }

        // Guard against a vacuous pass: the unfiltered full-area query really matches the lattice.
        (await inMemory.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None)).Should().HaveCount(125);
    }

    /// <summary>Cancels the caller's token just as the first data query is about to be sent.</summary>
    private sealed class CancelOnFirstReaderInterceptor(CancellationTokenSource cts)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
        }
    }
}
