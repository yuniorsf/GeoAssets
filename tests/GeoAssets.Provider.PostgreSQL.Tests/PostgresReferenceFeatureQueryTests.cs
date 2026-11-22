using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
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
/// XD01-330 — <see cref="PostgresReferenceFeatureQuery"/> against a real PostGIS instance, with rows of three
/// organizations (COELBA, the resource organization; BIMBO, the caller's; a third one) plus a shared row: only the
/// organization asked for is read, projected to the five reference fields, narrowed by type and scope; Row-Level Security
/// admits it under the non-owner app role; and the caller's own tenant-scoped queries in the same request scope are
/// untouched by the temporary tenant.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresReferenceFeatureQueryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string AppRole = "geoassets_app";

    // Same value the other app-role tests set — every class in the collection shares the container's role.
    private const string AppRolePassword = "geoassets_app_test_password";

    private static readonly Guid Coelba = Guid.Parse("c0e1ba00-0000-4000-8000-000000000330");
    private static readonly Guid Bimbo = Guid.Parse("b1b00000-0000-4000-8000-000000000330");
    private static readonly Guid Third = Guid.Parse("7e1d0000-0000-4000-8000-000000000330");

    private static readonly Guid CoelbaPoleType = Guid.Parse("c0e1ba00-0001-4000-8000-000000000330");
    private static readonly Guid ThirdPoleType = Guid.Parse("7e1d0000-0001-4000-8000-000000000330");
    private static readonly Guid SharedType = Guid.Parse("00000000-0001-4000-8000-000000000330");

    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly BoundingBox Box = new(0, 0, 10, 10);
    private static readonly GeoPolygon Area = new([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]);
    private static readonly ReferenceFeatureFilter Everything = new(null, null, int.MaxValue);

    public Task InitializeAsync() => ClearAsync();

    public async Task DisposeAsync()
    {
        await ClearAsync();
        // The app-role test opens pooled connections as geoassets_app; drop them so no later test class is handed one.
        NpgsqlConnection.ClearAllPools();
    }

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
        Guid[] ours = [CoelbaPoleType, ThirdPoleType, SharedType];
        await db.AssetTypes.Where(t => ours.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private static GeoFeature Feature(string id, Guid owner, GeoGeometry? geometry, Guid? assetType = null, string? scopeId = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"secret-{id}" };
        if (scopeId is not null) attributes["scopeId"] = scopeId;
        return new GeoFeature
        {
            Id = id,
            Geometry = geometry,
            Properties = new GeoFeatureProperties
            {
                Name = $"Label {id}",
                AssetTypeId = (assetType ?? CoelbaPoleType).ToString(),
                OrganizationId = owner,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime,
                CustomAttributes = attributes,
            },
        };
    }

    /// <summary>Seeds through the real provider, so <c>geo_entity_attribute</c> is populated exactly as in production.</summary>
    private async Task SeedThreeOrganizationsAsync()
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddAssetType(new AssetType { Id = CoelbaPoleType, Name = "COELBA pole", OrganizationId = Coelba });
        provider.AddAssetType(new AssetType { Id = ThirdPoleType, Name = "Third pole", OrganizationId = Third });
        provider.AddAssetType(new AssetType { Id = SharedType, Name = "Shared type" });
        provider.AddRange(
        [
            Feature("coelba-m1", Coelba, new GeoPoint(1, 1), scopeId: "municipio-1"),
            Feature("coelba-m2", Coelba, new GeoPoint(2, 2), scopeId: "municipio-2"),
            Feature("coelba-unscoped", Coelba, new GeoPoint(3, 3), SharedType),
            Feature("coelba-edge", Coelba, new GeoLineString([(10, 5), (20, 5)])),
            Feature("coelba-foreign-type", Coelba, new GeoPoint(4, 4), ThirdPoleType),
            Feature("coelba-outside", Coelba, new GeoPoint(50, 50)),
            Feature("coelba-no-geometry", Coelba, null),
            Feature("bimbo-1", Bimbo, new GeoPoint(5, 5)),
            Feature("third-1", Third, new GeoPoint(6, 6), ThirdPoleType, "municipio-1"),
            Feature("shared-1", Guid.Empty, new GeoPoint(7, 7), SharedType),
        ]);
    }

    private PostgresReferenceFeatureQuery NewSut() => new(fixture.CreateOptions());

    private static async Task<IReadOnlyList<string>> IdsAsync(Task<IReadOnlyList<ReferenceFeature>> query) =>
        [.. (await query).Select(f => f.Id)];

    // ── Isolation across the three organizations ─────────────────────────────

    [Fact]
    public async Task Reads_only_the_named_organizations_features_intersecting_the_box()
    {
        await SeedThreeOrganizationsAsync();
        var sut = NewSut();

        (await IdsAsync(sut.GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None)))
            .Should().Equal("coelba-edge", "coelba-foreign-type", "coelba-m1", "coelba-m2", "coelba-unscoped");
        (await IdsAsync(sut.GetInBoundsAsync(Third, Box, Everything, CancellationToken.None)))
            .Should().Equal("third-1");
        (await IdsAsync(sut.GetInBoundsAsync(Bimbo, Box, Everything, CancellationToken.None)))
            .Should().Equal("bimbo-1");
    }

    [Fact]
    public async Task An_organization_without_features_reads_nothing_and_the_shared_rows_are_not_included()
    {
        await SeedThreeOrganizationsAsync();

        (await NewSut().GetInBoundsAsync(Guid.Parse("0dd00000-0000-4000-8000-000000000330"), Box, Everything, CancellationToken.None))
            .Should().BeEmpty();
        (await IdsAsync(NewSut().GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None)))
            .Should().NotContain("shared-1");
    }

    [Fact]
    public async Task Projects_the_five_fields_with_the_asset_type_name_of_the_organizations_catalog()
    {
        await SeedThreeOrganizationsAsync();

        var result = await NewSut().GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None);

        result.Select(f => (f.Id, f.Label, f.AssetTypeName)).Should().Equal(
            ("coelba-edge", "Label coelba-edge", "COELBA pole"),
            ("coelba-foreign-type", "Label coelba-foreign-type", string.Empty),
            ("coelba-m1", "Label coelba-m1", "COELBA pole"),
            ("coelba-m2", "Label coelba-m2", "COELBA pole"),
            ("coelba-unscoped", "Label coelba-unscoped", "Shared type"));
        var m1 = result.Single(f => string.Equals(f.Id, "coelba-m1", StringComparison.Ordinal));
        m1.AssetTypeId.Should().Be(CoelbaPoleType.ToString());
        m1.Geometry.Should().BeOfType<GeoPoint>().Which.Coordinates.Should().Equal(1, 1);
        result.Single(f => string.Equals(f.Id, "coelba-edge", StringComparison.Ordinal)).Geometry.Should().BeOfType<GeoLineString>();
    }

    [Fact]
    public async Task Scopes_keep_only_the_features_of_the_grants_scopes()
    {
        await SeedThreeOrganizationsAsync();
        var sut = NewSut();

        (await IdsAsync(sut.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, ["municipio-1"], int.MaxValue), CancellationToken.None)))
            .Should().Equal("coelba-m1");
        (await IdsAsync(sut.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, ["municipio-1", "municipio-2"], int.MaxValue), CancellationToken.None)))
            .Should().Equal("coelba-m1", "coelba-m2");
        (await sut.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, [], int.MaxValue), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_asset_type_filter_and_the_cap_are_applied_in_id_order()
    {
        await SeedThreeOrganizationsAsync();
        var sut = NewSut();

        (await IdsAsync(sut.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(SharedType.ToString(), null, int.MaxValue), CancellationToken.None)))
            .Should().Equal("coelba-unscoped");
        (await IdsAsync(sut.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, null, 2), CancellationToken.None)))
            .Should().Equal("coelba-edge", "coelba-foreign-type");
    }

    [Fact]
    public async Task Pushes_the_box_scope_order_and_limit_down_to_Postgres_as_parameters()
    {
        await SeedThreeOrganizationsAsync();
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;

        var result = await new PostgresReferenceFeatureQuery(options)
            .GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, ["municipio-1"], 1), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("coelba-m1");
        var features = interceptor.CommandTexts.Should().Contain(s => s.Contains("ST_Intersects")).Which;
        features.Should().Contain("LIMIT").And.Contain("COLLATE \"C\"").And.Contain("geo_entity_attribute").And.Contain("\"OrganizationId\" = @");
        features.Should().NotContain("municipio-1", "values travel as parameters, never as SQL text");
        features.Should().NotContain("custom_attributes", "the attributes are never read");
        features.Should().NotContain("topology", "the topology is never read");
    }

    [Fact]
    public async Task As_the_non_owner_app_role_Row_Level_Security_admits_exactly_the_named_organization()
    {
        await SeedThreeOrganizationsAsync();
        await using (var admin = new GeoAssetsDbContext(fixture.CreateOptions()))
        {
            await admin.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD '{AppRolePassword}'");
            // asset_type has its tenant policy but no grant to the app role yet (AddAssetTypeRowLevelSecurity's note);
            // the security suite grants it the same way.
            await admin.Database.ExecuteSqlRawAsync($"GRANT SELECT ON asset_type TO {AppRole}");
        }

        // A connection string of its own, so its pool too: EF Core's Npgsql data source keeps a pool that ClearAllPools does
        // not reach, and a session another class's app-role test already stamped reads the setting as '' (not NULL), which
        // the unstamped control below could not cast to uuid.
        var appRoleConnectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = AppRole,
            Password = AppRolePassword,
            ApplicationName = nameof(PostgresReferenceFeatureQueryTests),
        }.ConnectionString;
        var appRoleOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(appRoleConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        // Control: with nothing stamped, the policy hides every row from this role.
        await using (var unstamped = new GeoAssetsDbContext(appRoleOptions))
            (await unstamped.GeoEntities.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var sut = new PostgresReferenceFeatureQuery(appRoleOptions);
        var coelba = await sut.GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None);
        var third = await IdsAsync(sut.GetInBoundsAsync(Third, Box, Everything, CancellationToken.None));

        coelba.Select(f => f.Id).Should().Equal("coelba-edge", "coelba-foreign-type", "coelba-m1", "coelba-m2", "coelba-unscoped");
        coelba.Single(f => string.Equals(f.Id, "coelba-m1", StringComparison.Ordinal)).AssetTypeName.Should().Be("COELBA pole");
        third.Should().Equal("third-1");

        // The stamp was transaction-local: the pooled connection carries no organization over to its next user.
        await using var pooled = new NpgsqlConnection(appRoleConnectionString);
        await pooled.OpenAsync();
        await using var readSetting = pooled.CreateCommand();
        readSetting.CommandText = "SELECT coalesce(current_setting('app.current_organization_id', true), '')";
        (await readSetting.ExecuteScalarAsync()).Should().Be(string.Empty);
    }

    // ── The request's own tenant ─────────────────────────────────────────────

    [Fact]
    public async Task The_callers_own_tenant_queries_in_the_same_request_scope_are_unaffected()
    {
        await SeedThreeOrganizationsAsync();

        var services = new ServiceCollection();
        services.AddPostgresAssetSpatialQuery(fixture.ConnectionString);
        services.AddPostgresReferenceFeatureQuery(fixture.ConnectionString);
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(Bimbo));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var request = provider.CreateAsyncScope();
        var tenant = request.ServiceProvider.GetRequiredService<ITenantContext>();
        var own = request.ServiceProvider.GetRequiredService<IAssetSpatialQuery>();
        var reference = request.ServiceProvider.GetRequiredService<IReferenceFeatureQuery>();
        var ownFilter = new AssetSpatialFilter(null, null, int.MaxValue);

        var before = await own.FindCoveredByAsync(Area, ownFilter, CancellationToken.None);
        var coelba = await IdsAsync(reference.GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None));
        var after = await own.FindCoveredByAsync(Area, ownFilter, CancellationToken.None);
        var interleaved = await Task.WhenAll(
            own.FindCoveredByAsync(Area, ownFilter, CancellationToken.None),
            own.FindByAttributeValuesAsync(AssetSpatialQueryKeys.FeatureId, ["bimbo-1", "coelba-m1", "third-1"], ownFilter, CancellationToken.None));

        reference.Should().BeOfType<PostgresReferenceFeatureQuery>();
        coelba.Should().Contain("coelba-m1").And.NotContain("bimbo-1");
        before.Select(f => f.Id).Should().Equal("bimbo-1");
        after.Select(f => f.Id).Should().Equal("bimbo-1");
        interleaved.Should().AllSatisfy(r => r.Select(f => f.Id).Should().Equal("bimbo-1"));
        tenant.CurrentOrganizationId.Should().Be(Bimbo);
    }

    // ── Registration and guards ──────────────────────────────────────────────

    [Fact]
    public void AddPostgresReferenceFeatureQuery_ReplacesAnEarlierRegistration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IReferenceFeatureQuery>(_ => throw new InvalidOperationException("replaced"));

        services.AddPostgresReferenceFeatureQuery(fixture.ConnectionString);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IReferenceFeatureQuery>().Should().ContainSingle().Which.Should().BeOfType<PostgresReferenceFeatureQuery>();
    }

    [Fact]
    public async Task Guards()
    {
        var sut = NewSut();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, null, 0), CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, Everything, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        FluentActions.Invoking(() => new PostgresReferenceFeatureQuery(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection().AddPostgresReferenceFeatureQuery(" ")).Should().Throw<ArgumentException>();
    }
}
