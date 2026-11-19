using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-340 end to end, through <c>GeoAssets.Server</c>'s own asset endpoints
/// (<see cref="GeoAssetsRestApiExtensions.MapGeoAssetsApi"/>) on a database built by the production
/// migrations, with <c>Program.cs</c>'s catalog registrations (as in
/// <see cref="OrganizationScopedAssetReadsEndToEndTests"/>) and the request's organization
/// resolved by <see cref="HttpTenantContext"/> from the caller's claims:
/// <list type="bullet">
///   <item>a <c>PUT</c> of another organization's feature is 404, whatever organization the body
///   states, and leaves its row — and its attribute rows — untouched; the caller's own feature is
///   updated and stays its own;</item>
///   <item><c>POST</c>, bulk add and load stamp the caller's organization on every feature,
///   ignoring the body's; a bulk add naming another organization's feature is 404 and changes
///   nothing;</item>
///   <item>a shared (organization-less) feature stays shared when an organization updates it, and a
///   caller without an organization writes as before — except that it cannot overwrite an
///   organization's feature either.</item>
/// </list>
/// Each test gets its own database in the suite's container.
/// </summary>
public sealed class OrganizationScopedAssetWritesEndToEndTests
{
    private const string PoleType = "osw-pole";
    private const string OrganizationClaim = "test_org";
    private const string OrganizationHeader = "X-Test-Organization";
    private const string NoOrganization = "none";

    private static readonly Guid Owner = new("b5b5b5b5-0000-4000-8000-000000000340");
    private static readonly Guid Other = new("b6b6b6b6-0000-4000-8000-000000000340");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const double Lon = -38.5;
    private const double Lat = -12.9;

    private const string OwnersPole = "osw-pole-owner";
    private const string OthersPole = "osw-pole-other";
    private const string SharedPole = "osw-pole-shared";

    // ── PUT ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_OfAnotherOrganizationsFeature_Is404_WhateverTheBodyStates_AndLeavesItsRowUntouched()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        foreach (var stated in new[] { Owner, Other, Guid.Empty })
        {
            var hijack = Pole(OthersPole, stated, 0.5);
            hijack.Properties.Name = "hijacked";
            hijack.Properties.CustomAttributes["plate"] = "stolen";

            using var response = await owner.PutAsync(Uri($"features/{OthersPole}"), Json(hijack));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await host.RowAsync(OthersPole)).Should().Be(new Row(OthersPole, Other));
        (await host.AttributesAsync(OthersPole)).Should().Equal($"plate=P-{OthersPole}");
        (await FeatureAsync(other, OthersPole)).Properties.Name.Should().Be(OthersPole);

        using var unknown = await owner.PutAsync(Uri("features/osw-pole-unknown"), Json(Pole("osw-pole-unknown", Owner, 0.6)));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound, "another organization's id is answered as an unknown one");
    }

    [Fact]
    public async Task Put_OfTheCallersOwnFeature_UpdatesIt_AndItStaysTheCallers()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        var renamed = Pole(OwnersPole, Other, 0.01);
        renamed.Properties.Name = "renamed";

        using var response = await owner.PutAsync(Uri($"features/{OwnersPole}"), Json(renamed));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.RowAsync(OwnersPole)).Should().Be(new Row("renamed", Owner));
        (await FeatureAsync(owner, OwnersPole)).Properties.Name.Should().Be("renamed");
        (await other.GetAsync(Uri($"features/{OwnersPole}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST, bulk, load ──────────────────────────────────────────────────────

    [Fact]
    public async Task PostBulkAndLoad_WithAnotherOrganizationInTheBody_AreStampedWithTheCallersOrganization()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        using (var posted = await owner.PostAsync(Uri("features"), Json(Pole("osw-posted", Other, 0.04))))
            posted.StatusCode.Should().Be(HttpStatusCode.Created);
        (await host.RowAsync("osw-posted")).Should().Be(new Row("osw-posted", Owner));
        (await other.GetAsync(Uri("features/osw-posted"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var bulk = await owner.PostAsync(Uri("features/bulk"), Json(new[] { Pole("osw-bulk-other", Other, 0.05), Pole("osw-bulk-shared", Guid.Empty, 0.06) })))
            bulk.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.RowAsync("osw-bulk-other")).Should().Be(new Row("osw-bulk-other", Owner));
        (await host.RowAsync("osw-bulk-shared")).Should().Be(new Row("osw-bulk-shared", Owner));
        (await IdsAsync(other)).Should().BeEquivalentTo([OthersPole, SharedPole]);

        using (var load = await owner.PostAsync(Uri("features/load"), Json(new[] { Pole("osw-loaded", Other, 0.07) })))
            load.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.RowAsync("osw-loaded")).Should().Be(new Row("osw-loaded", Owner));
        (await IdsAsync(owner)).Should().BeEquivalentTo(["osw-loaded", SharedPole], "a load replaces the caller's own features");
        (await IdsAsync(other)).Should().BeEquivalentTo([OthersPole, SharedPole]);
    }

    [Fact]
    public async Task BulkAndLoad_NamingAnotherOrganizationsFeature_Are404_AndChangeNothing()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);

        var hijack = Pole(OthersPole, Owner, 0.5);
        hijack.Properties.Name = "hijacked";

        using (var bulk = await owner.PostAsync(Uri("features/bulk"), Json(new[] { Pole("osw-bulk-new", Owner, 0.05), hijack })))
            bulk.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var load = await owner.PostAsync(Uri("features/load"), Json(new[] { hijack })))
            load.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await host.RowAsync("osw-bulk-new")).Should().BeNull();
        (await host.RowAsync(OwnersPole)).Should().Be(new Row(OwnersPole, Owner), "a refused load removes nothing");
        (await host.RowAsync(OthersPole)).Should().Be(new Row(OthersPole, Other));
        (await host.AttributesAsync(OthersPole)).Should().Equal($"plate=P-{OthersPole}");
    }

    // ── Shared features, callers without an organization ─────────────────────

    [Fact]
    public async Task ASharedFeature_StaysShared_WhenAnOrganizationUpdatesIt()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        var renamed = Pole(SharedPole, Owner, 0.03);
        renamed.Properties.Name = "renamed by the owner";
        using (var put = await owner.PutAsync(Uri($"features/{SharedPole}"), Json(renamed)))
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.RowAsync(SharedPole)).Should().Be(new Row("renamed by the owner", Guid.Empty));
        (await FeatureAsync(other, SharedPole)).Properties.Name.Should().Be("renamed by the owner");
        (await IdsAsync(owner)).Should().BeEquivalentTo([OwnersPole, SharedPole]);

        // The shared catalog is one provider for the whole host: further PUTs of the same feature
        // through it succeed and store the latest values (XD01-341).
        var renamedAgain = Pole(SharedPole, Owner, 0.03);
        renamedAgain.Properties.Name = "renamed by the owner again";
        renamedAgain.Properties.CustomAttributes["plate"] = "P-again";
        using (var put = await owner.PutAsync(Uri($"features/{SharedPole}"), Json(renamedAgain)))
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var renamedByOther = Pole(SharedPole, Other, 0.03);
        renamedByOther.Properties.Name = "renamed by the other";
        renamedByOther.Properties.CustomAttributes["plate"] = "P-other";
        using (var put = await other.PutAsync(Uri($"features/{SharedPole}"), Json(renamedByOther)))
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await host.RowAsync(SharedPole)).Should().Be(new Row("renamed by the other", Guid.Empty));
        (await host.AttributesAsync(SharedPole)).Should().Equal("plate=P-other");
        (await FeatureAsync(owner, SharedPole)).Properties.Name.Should().Be("renamed by the other");
    }

    [Fact]
    public async Task Put_TwiceOfTheCallersOwnFeature_BothSucceed()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);

        foreach (var name in new[] { "first", "second" })
        {
            var renamed = Pole(OwnersPole, Owner, 0.01);
            renamed.Properties.Name = name;
            renamed.Properties.CustomAttributes["plate"] = $"P-{name}";
            using var response = await owner.PutAsync(Uri($"features/{OwnersPole}"), Json(renamed));
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await host.RowAsync(OwnersPole)).Should().Be(new Row("second", Owner));
        (await host.AttributesAsync(OwnersPole)).Should().Equal("plate=P-second");
    }

    [Fact]
    public async Task Bulk_OfExistingFeatures_UpdatesThem_AndASharedOneStaysShared()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        foreach (var round in new[] { 1, 2 })
        {
            var own = Pole(OwnersPole, Owner, 0.01);
            own.Properties.Name = $"own bulk {round}";
            own.Properties.CustomAttributes["plate"] = $"P-own-{round}";
            var shared = Pole(SharedPole, Owner, 0.03);
            shared.Properties.Name = $"shared bulk {round}";
            shared.Properties.CustomAttributes["plate"] = $"P-shared-{round}";

            using var bulk = await owner.PostAsync(Uri("features/bulk"), Json(new[] { own, shared, Pole($"osw-bulk-new-{round}", Owner, 0.05 + round / 100.0) }));

            bulk.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await host.RowAsync(OwnersPole)).Should().Be(new Row("own bulk 2", Owner));
        (await host.AttributesAsync(OwnersPole)).Should().Equal("plate=P-own-2");
        (await host.RowAsync(SharedPole)).Should().Be(new Row("shared bulk 2", Guid.Empty));
        (await host.AttributesAsync(SharedPole)).Should().Equal("plate=P-shared-2");
        (await FeatureAsync(owner, OwnersPole)).Properties.Name.Should().Be("own bulk 2");
        (await FeatureAsync(other, SharedPole)).Properties.Name.Should().Be("shared bulk 2");
        (await IdsAsync(owner)).Should().BeEquivalentTo([OwnersPole, SharedPole, "osw-bulk-new-1", "osw-bulk-new-2"]);
    }

    [Fact]
    public async Task AnOrganizationLessCaller_WritesAsBefore_ButCannotOverwriteAnOrganizationsFeature()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var organizationLess = host.ClientFor(organizationId: null);

        var renamedAgain = Pole(SharedPole, Guid.Empty, 0.03);
        renamedAgain.Properties.Name = "renamed without an organization";
        using (var put = await organizationLess.PutAsync(Uri($"features/{SharedPole}"), Json(renamedAgain)))
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.RowAsync(SharedPole)).Should().Be(new Row("renamed without an organization", Guid.Empty));

        using (var posted = await organizationLess.PostAsync(Uri("features"), Json(Pole("osw-no-org", Guid.Empty, 0.08))))
            posted.StatusCode.Should().Be(HttpStatusCode.Created);
        (await host.RowAsync("osw-no-org")).Should().Be(new Row("osw-no-org", Guid.Empty));
        (await IdsAsync(owner)).Should().Contain("osw-no-org");

        var hijack = Pole(OwnersPole, Guid.Empty, 0.5);
        hijack.Properties.Name = "hijacked";
        using (var put = await organizationLess.PutAsync(Uri($"features/{OwnersPole}"), Json(hijack)))
            put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.RowAsync(OwnersPole)).Should().Be(new Row(OwnersPole, Owner));
        (await host.AttributesAsync(OwnersPole)).Should().Equal($"plate=P-{OwnersPole}");
    }

    // ── HTTP ──────────────────────────────────────────────────────────────────

    private static Uri Uri(string path) => new($"/api/geoassets/{path}", UriKind.Relative);

    private static StringContent Json<T>(T value) =>
        new(JsonSerializer.Serialize(value, GeoJsonSerializer.Options), System.Text.Encoding.UTF8, "application/json");

    private static async Task<IEnumerable<string>> IdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Uri("features"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var features = JsonSerializer.Deserialize<List<GeoFeature>>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
        return features.Select(f => f.Id).Where(id => id.StartsWith("osw-", StringComparison.Ordinal));
    }

    private static async Task<GeoFeature> FeatureAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync(Uri($"features/{id}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<GeoFeature>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    private sealed record Row(string Name, Guid OrganizationId);

    private sealed class Host(WebApplication web, string connectionString) : IAsyncDisposable
    {
        /// <summary>A client authenticated as a user of <paramref name="organizationId"/>, or of none.</summary>
        public HttpClient ClientFor(Guid? organizationId)
        {
            var client = web.GetTestServer().CreateClient();
            client.DefaultRequestHeaders.Add(OrganizationHeader, organizationId?.ToString() ?? NoOrganization);
            return client;
        }

        /// <summary>The feature's row as stored, whatever its organization.</summary>
        public async Task<Row?> RowAsync(string id)
        {
            await using var db = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString));
            return await db.GeoEntities.IgnoreQueryFilters()
                .Where(row => row.Id == id)
                .Select(row => new Row(row.Name, row.OrganizationId))
                .SingleOrDefaultAsync();
        }

        public async Task<List<string>> AttributesAsync(string id)
        {
            await using var db = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString));
            return await db.GeoEntityAttributes
                .Where(a => a.EntityId == id)
                .OrderBy(a => a.Key)
                .Select(a => a.Key + "=" + a.Value)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await web.StopAsync();
            await web.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// A fresh database migrated with the production chain and the three poles in the catalog;
    /// then a test server with the registrations and middleware <c>Program.cs</c> uses for the
    /// catalog, and the asset endpoints.
    /// </summary>
    private static async Task<Host> HostAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"org_scoped_writes_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
            await geoAssets.Database.MigrateAsync();

        await SeedCatalogAsync(connectionString);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;

        // Program.cs's catalog registrations.
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddServerOrganizationAssetCatalogs(connectionString);
        services.AddGeoAssetsDbContextFactory(connectionString);
        services.AddSingleton<WmsPostGisRenderer>();

        // The caller and their organization, resolved as Program.cs resolves them.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = [OrganizationClaim] }));
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddGeoAuthorizationPolicyBridge();
        services.AddScoped<IGeoAuthorizationService, PermittingAuthorizationService>();
        services.AddSingleton<IOrganizationGrantRepository, NoGrants>();

        var web = builder.Build();
        await web.Services.WarmPostgresAssetProviderAsync();
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapGeoAssetsApi();
        await web.StartAsync();

        return new Host(web, connectionString);
    }

    /// <summary>One pole for each organization and one shared pole, before the host warms its shared catalog.</summary>
    private static async Task SeedCatalogAsync(string connectionString)
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
        await using var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        catalog.AddRange([Pole(OwnersPole, Owner, 0.01), Pole(OthersPole, Other, 0.02), Pole(SharedPole, Guid.Empty, 0.03)]);
    }

    private static GeoFeature Pole(string id, Guid organizationId, double offset) => new()
    {
        Id = id,
        Geometry = new GeoPoint(Lon + offset, Lat + offset),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = PoleType,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{id}" },
        },
    };

    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates any request carrying <see cref="OrganizationHeader"/> as a user whose
    /// organization claim is the header's value (none for <see cref="NoOrganization"/>).
    /// </summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrganizationHeader, out var organization))
                return Task.FromResult(AuthenticateResult.NoResult());

            List<Claim> claims = [new("oid", "user-1")];
            if (!string.Equals(organization.ToString(), NoOrganization, StringComparison.Ordinal))
                claims.Add(new Claim(OrganizationClaim, organization.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>Grants every permission; the caller's organization is the request's tenant.</summary>
    private sealed class PermittingAuthorizationService(ITenantContext tenant) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    DisplayName = "User",
                    CreatedAt = SeedTime,
                    OrganizationId = tenant.CurrentOrganizationId == Guid.Empty ? null : tenant.CurrentOrganizationId,
                },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    private sealed class NoGrants : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
