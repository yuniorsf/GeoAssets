using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-338 end to end, through <c>GeoAssets.Server</c>'s own asset endpoints
/// (<see cref="GeoAssetsRestApiExtensions.MapGeoAssetsApi"/>) on a database built by the production
/// migrations, with <c>Program.cs</c>'s catalog registrations — the warmed tenant-less provider,
/// <see cref="OrganizationAssetCatalogHostExtensions.AddServerOrganizationAssetCatalogs"/> and
/// <see cref="OrganizationAssetCatalogHostExtensions.UseOrganizationAssetCatalogs"/> — and the
/// request's organization resolved by <see cref="HttpTenantContext"/> from the caller's claims:
/// <list type="bullet">
///   <item>each organization reads its own poles and the shared one, never the other's;</item>
///   <item>a caller without an organization reads the shared pole only, as before;</item>
///   <item>XD01-337's scenario: once an approval's occupancy batch is applied in process, the owner
///   reads the pole's new <c>occupancyClass</c> through the feature and viewport endpoints — even
///   though its catalog was already loaded before the approval.</item>
/// </list>
/// The database gets its own name in the suite's container.
/// </summary>
public sealed class OrganizationScopedAssetReadsEndToEndTests
{
    private const string Order = "osr-order";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";
    private const string OrganizationClaim = "test_org";
    private const string OrganizationHeader = "X-Test-Organization";
    private const string NoOrganization = "none";

    private static readonly Guid Owner = new("a5a5a5a5-0000-4000-8000-000000000338");
    private static readonly Guid Other = new("a6a6a6a6-0000-4000-8000-000000000338");
    private static readonly Guid Occupant = new("a7a7a7a7-0000-4000-8000-000000000338");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly WorkflowPrincipal Approver = new("approver-1", Owner.ToString(), ["Supervisor"], [], []);

    private const double Lon = -38.5;
    private const double Lat = -12.9;

    private const string OwnersPole = "osr-pole-owner";
    private const string OthersPole = "osr-pole-other";
    private const string SharedPole = "osr-pole-shared";

    [Fact]
    public async Task EachOrganization_ReadsItsOwnAndTheSharedPoles_NeverTheOthers_AndAnOrganizationLessCallerReadsTheSharedOnly()
    {
        await using var host = await HostAsync();

        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);
        using var organizationLess = host.ClientFor(organizationId: null);

        (await IdsAsync(owner, "features")).Should().BeEquivalentTo([OwnersPole, SharedPole]);
        (await IdsAsync(other, "features")).Should().BeEquivalentTo([OthersPole, SharedPole]);
        (await IdsAsync(organizationLess, "features")).Should().BeEquivalentTo([SharedPole]);

        (await IdsAsync(owner, Bounds)).Should().BeEquivalentTo([OwnersPole, SharedPole]);
        (await IdsAsync(other, Bounds)).Should().BeEquivalentTo([OthersPole, SharedPole]);
        (await IdsAsync(organizationLess, Bounds)).Should().BeEquivalentTo([SharedPole]);

        (await owner.GetAsync(Uri($"features/{OthersPole}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync(Uri($"features/{OwnersPole}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await organizationLess.GetAsync(Uri($"features/{OwnersPole}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync(Uri($"features/{SharedPole}"))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var page = await owner.PostAsync(Uri("features/query"), Json(new AssetQuery { AssetTypeId = PoleType, Take = 10 }));
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = JsonSerializer.Deserialize<PagedResult<GeoFeature>>(await page.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
        paged.TotalCount.Should().Be(2);
        paged.Items.Select(f => f.Id).Should().BeEquivalentTo([OwnersPole, SharedPole]);
    }

    [Fact]
    public async Task AfterAnApprovalIsAppliedInProcess_TheOwnerReadsThePolesNewOccupancyClass_ThroughTheFeatureAndViewportEndpoints()
    {
        await using var host = await HostAsync();
        using var owner = host.ClientFor(Owner);
        using var other = host.ClientFor(Other);

        var before = await FeatureAsync(owner, OwnersPole);
        before.Properties.CustomAttributes.Should().NotContainKey(OccupancyAssetAttributes.ClassKey, "the owner's catalog is loaded before the approval");

        await host.StageAsync(OwnersPole, new OccupancyChange
        {
            Kind = OccupancyChangeKind.Add,
            SlotIndex = 1,
            EntryId = Guid.NewGuid(),
            Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
            OccupantOrganizationId = Occupant,
            OperatorCnpj = "98.765.432/0001-10",
            CableDiameterMm = 9.5m,
            CableLoadDaN = 80m,
        });
        (await host.ApproveAsync()).PolesUpdated.Should().Be(1);
        (await host.Relay.ProcessNextBatchAsync(CancellationToken.None)).Should().Be(1);

        var after = await FeatureAsync(owner, OwnersPole);
        after.Properties.CustomAttributes.Should().Contain(OccupancyAssetAttributes.ClassKey, "Low");
        after.Properties.CustomAttributes.Should().Contain("plate", $"P-{OwnersPole}");

        var inViewport = (await FeaturesAsync(owner, Bounds)).Single(f => string.Equals(f.Id, OwnersPole, StringComparison.Ordinal));
        inViewport.Properties.CustomAttributes.Should().Contain(OccupancyAssetAttributes.ClassKey, "Low");

        (await other.GetAsync(Uri($"features/{OwnersPole}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── HTTP ──────────────────────────────────────────────────────────────────

    private const string Bounds = "features/bounds?minLon=-39&minLat=-13.5&maxLon=-38&maxLat=-12.5";

    private static Uri Uri(string path) => new($"/api/geoassets/{path}", UriKind.Relative);

    private static StringContent Json<T>(T value) =>
        new(JsonSerializer.Serialize(value, GeoJsonSerializer.Options), System.Text.Encoding.UTF8, "application/json");

    private static async Task<IReadOnlyList<GeoFeature>> FeaturesAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(Uri(path));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<List<GeoFeature>>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
    }

    private static async Task<IEnumerable<string>> IdsAsync(HttpClient client, string path) =>
        (await FeaturesAsync(client, path)).Select(f => f.Id).Where(id => id.StartsWith("osr-", StringComparison.Ordinal));

    private static async Task<GeoFeature> FeatureAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync(Uri($"features/{id}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<GeoFeature>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    private sealed class Host(WebApplication web) : IAsyncDisposable
    {
        public IServiceProvider Services => web.Services;

        public OutboxRelayProcessor Relay => Services.GetRequiredService<OutboxRelayProcessor>();

        /// <summary>A client authenticated as a user of <paramref name="organizationId"/>, or of none.</summary>
        public HttpClient ClientFor(Guid? organizationId)
        {
            var client = web.GetTestServer().CreateClient();
            client.DefaultRequestHeaders.Add(OrganizationHeader, organizationId?.ToString() ?? NoOrganization);
            return client;
        }

        public async Task StageAsync(string pole, OccupancyChange change)
        {
            await using var scope = Services.CreateAsyncScope();
            var baseline = await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync(pole, CancellationToken.None);
            var set = OccupancyChangeSet.Stage(
                Order, OrderTargetSelectionService.TargetId(Order, pole), pole, Owner, [change], baseline, "crew-1", new DateTimeOffset(SeedTime));
            await scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetWriter>().StageAsync(set, CancellationToken.None);
        }

        public async Task<OccupancyCommitResult> ApproveAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>()
                .ApproveAndCommitAsync(Order, Approver, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await web.StopAsync();
            await web.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// A fresh database migrated with the production chains, the three poles in the catalog and a
    /// completed pole-work order of the owner's targeting its pole; then a test server with the
    /// registrations and middleware <c>Program.cs</c> uses for the catalog, the occupancy commit
    /// and its in-process apply (the relay driven by the test instead of running as a hosted
    /// service), and the asset endpoints.
    /// </summary>
    private static async Task<Host> HostAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"org_scoped_reads_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
        {
            await geoAssets.Database.MigrateAsync();
            await geoAssets.Database.ExecuteSqlRawAsync($"""
                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity_attribute TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON asset_type TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON applied_mutation_batches TO {SecurityTestContainerFixture.TenantScopedRole};
                """);
        }

        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
            await identity.Database.MigrateAsync();
        await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            await serviceOrders.Database.MigrateAsync();
        await SecurityTestContainerFixture.MigrateNeutralOperatorAsync(connectionString);

        await SeedCatalogAsync(connectionString);

        // The batch is applied as the suite's non-owner role, as in XD01-337's end-to-end test.
        var catalogConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
        }.ConnectionString;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Program.cs's catalog registrations.
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        services.AddServerOrganizationAssetCatalogs();
        services.AddGeoAssetsDbContextFactory(connectionString);
        services.AddPostgresAssetRenderQuery(connectionString);

        // The caller and their organization, resolved as Program.cs resolves them; outside a
        // request (the test arranging the order) the owner's.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = [OrganizationClaim] }));
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
            ? new FixedTenant(Owner)
            : new HttpTenantContext(sp.GetRequiredService<ICurrentUserAccessor>()));
        services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddGeoAuthorizationPolicyBridge();
        services.AddScoped<IGeoAuthorizationService, PermittingAuthorizationService>();
        services.AddSingleton<IOrganizationGrantRepository, NoGrants>();

        // The occupancy commit and its in-process apply, as in XD01-337's end-to-end test.
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(new GeoAssetsMeter("1.0.0"));
        services.AddOrderTypeRegistry(registry => registry.RegisterSharedUseOrderTypes());
        services.AddWorkflowPersistence(o => o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
        services.AddNeutralOperatorPersistence();
        services.AddServiceOrderRules();
        services.AddServerOrderOccupancyStaging(configuration);
        services.AddServerWorkflowMessaging(configuration);
        services.AddServerOccupancyCommit(configuration);
        services.AddPostgresAssetMutationBatchApplier(catalogConnectionString);
        services.AddServerAssetMutationApply(configuration);
        var relay = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxRelayProcessor));
        services.Remove(relay);
        services.AddSingleton<OutboxRelayProcessor>();

        var web = builder.Build();
        await web.Services.WarmPostgresAssetProviderAsync();
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapGeoAssetsApi();
        await web.StartAsync();

        var host = new Host(web);
        await SeedOrderAsync(host);
        return host;
    }

    // ── Arrange ───────────────────────────────────────────────────────────────

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

    private static async Task SeedOrderAsync(Host host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder
        {
            Id = Order,
            Title = "Pole work",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            Status = ServiceOrderStatus.Completed,
            CreatedBy = "planner-1",
            OrganizationId = Owner,
            CreatedAt = SeedTime,
        });
        await scope.ServiceProvider.GetRequiredService<IServiceOrderTargetWriter>().AddRangeAsync(
            Order,
            [
                new ServiceOrderTarget
                {
                    Id = OrderTargetSelectionService.TargetId(Order, OwnersPole),
                    OrderId = Order,
                    FeatureId = OwnersPole,
                    AssetTypeId = PoleType,
                    ActivityIds = ["relocate-attachment"],
                    Status = OrderTargetStatus.Pending,
                    AddedAt = SeedTime,
                    AddedBy = "planner-1",
                    OrganizationId = Owner,
                },
            ],
            CancellationToken.None);
    }

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

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
