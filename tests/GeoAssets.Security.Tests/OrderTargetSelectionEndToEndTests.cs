using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// End to end for XD01-308: the real <c>MapServiceOrderTargetsApi()</c> endpoints in a
/// TestServer, wired as <c>GeoAssets.Server/Program.cs</c> wires them — the selection service
/// with its observability decorator, the EF order and target stores, and the PostGIS spatial
/// query — over a PostgreSQL database built by the production migrations.
///
/// Each test gets a database of its own in the suite's container, so it cannot disturb, or be
/// disturbed by, the row-level-security tests that truncate the shared one.
/// </summary>
public sealed class OrderTargetSelectionEndToEndTests
{
    private const string TokenHeader = "X-Test-Token";
    private const string PoleSurvey = "pole-survey";

    private static readonly Guid OrgA = new("a1a1a1a1-0000-4000-8000-00000e2e0308");
    private static readonly Guid OrgB = new("b2b2b2b2-0000-4000-8000-00000e2e0308");
    private static readonly Guid AdminA = new("00000000-0000-4000-8000-0000000e2ea1");
    private static readonly Guid AdminB = new("00000000-0000-4000-8000-0000000e2eb1");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, (Guid UserId, Guid OrganizationId)> Identities =
        new Dictionary<string, (Guid, Guid)>(StringComparer.Ordinal)
        {
            ["admin-a"] = (AdminA, OrgA),
            ["admin-b"] = (AdminB, OrgB),
        };

    private static readonly double[][][] Fence = [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]];

    [Fact]
    public async Task PolygonFenceThenIdentifierList_PreviewApplyPageAndExtent_AgainstSeededAssets()
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAssetsAsync();
        await app.SeedOrderAsync("ord-a", OrgA, AdminA);
        using var client = app.ClientFor("admin-a");

        // Preview a fence: nothing is written.
        var preview = await PostAsync(client, "service-orders/ord-a/targets/selection/preview", FenceBody(pageSize: 2));
        preview.GetProperty("matched").GetInt32().Should().Be(3);
        preview.GetProperty("firstPage").GetProperty("totalCount").GetInt32().Should().Be(3);
        preview.GetProperty("firstPage").GetProperty("items").EnumerateArray().Select(t => t.GetProperty("featureId").GetString())
            .Should().Equal("a-pole-1", "a-pole-2");
        (await GetAsync(client, "service-orders/ord-a/targets/count")).GetProperty("count").GetInt32().Should().Be(0);

        // Apply it, then repeat it: the second call adds nothing.
        var applied = await PostAsync(client, "service-orders/ord-a/targets/selection/apply", FenceBody());
        applied.GetProperty("added").GetInt32().Should().Be(3);
        var repeated = await PostAsync(client, "service-orders/ord-a/targets/selection/apply", FenceBody());
        repeated.GetProperty("added").GetInt32().Should().Be(0);
        repeated.GetProperty("alreadyInOrder").GetInt32().Should().Be(3);

        // Apply an identifier list on the policy's alias attribute: one held, one new, one unknown.
        var byPlate = await PostAsync(
            client,
            "service-orders/ord-a/targets/selection/apply",
            new { strategyId = "identifier-list", parameters = new { values = new[] { "P-300", "P-900", "P-404" } } });
        byPlate.GetProperty("added").GetInt32().Should().Be(1);
        byPlate.GetProperty("alreadyInOrder").GetInt32().Should().Be(1);
        byPlate.GetProperty("notFoundValues").EnumerateArray().Select(v => v.GetString()).Should().Equal("P-404");

        // Page the targets: every target once, in feature-id order.
        var first = await GetAsync(client, "service-orders/ord-a/targets?page=1&pageSize=3");
        var second = await GetAsync(client, "service-orders/ord-a/targets?page=2&pageSize=3");
        first.GetProperty("totalCount").GetInt32().Should().Be(4);
        var paged = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToList();
        paged.Select(t => t.GetProperty("featureId").GetString()).Should().Equal("a-pole-1", "a-pole-2", "a-pole-3", "a-pole-far");
        paged.Select(t => t.GetProperty("alias").GetString()).Should().Equal("P-100", "P-200", "P-300", "P-900");
        paged.Should().OnlyContain(t =>
            t.GetProperty("organizationId").GetGuid() == OrgA && t.GetProperty("addedBy").GetGuid() == AdminA);

        // The extent is the bounding box of the targets' centroids.
        (await GetAsync(client, "service-orders/ord-a/targets/extent")).GetProperty("bbox").EnumerateArray().Select(n => n.GetDouble())
            .Should().Equal(1, 1, 50, 50);

        // The order records the last selection and one Annotate entry per apply.
        var order = await app.LoadOrderAsync("ord-a", OrgA);
        order.SelectionSpec!.StrategyId.Should().Be("identifier-list");
        order.ActionLog.Select(e => e.Action).Should().Equal(OrderActionType.Annotate, OrderActionType.Annotate, OrderActionType.Annotate);
        order.ActionLog.Select(e => e.PerformedBy).Distinct(StringComparer.Ordinal).Should().Equal(AdminA.ToString());
    }

    [Fact]
    public async Task TwoOrganizations_ASelectionForOrganizationAsOrder_NeverReturnsOrganizationBsAssets()
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAssetsAsync();
        await app.SeedOrderAsync("ord-a", OrgA, AdminA);
        await app.SeedOrderAsync("ord-b", OrgB, AdminB);
        using var asA = app.ClientFor("admin-a");
        using var asB = app.ClientFor("admin-b");
        const string applyA = "service-orders/ord-a/targets/selection/apply";

        // A fence that covers poles of both organizations, and a plate both organizations use.
        var fence = await PostAsync(asA, applyA, FenceBody());
        var plate = await PostAsync(
            asA, applyA, new { strategyId = "identifier-list", parameters = new { values = new[] { "P-100", "P-700" } } });
        var targetsOfA = await GetAsync(asA, "service-orders/ord-a/targets?pageSize=200");

        fence.GetProperty("matched").GetInt32().Should().Be(3);
        plate.GetProperty("matched").GetInt32().Should().Be(1, "P-100 is also a plate of organization B, and P-700 only of B");
        plate.GetProperty("notFoundValues").EnumerateArray().Select(v => v.GetString()).Should().Equal("P-700");
        targetsOfA.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("featureId").GetString())
            .Should().Equal("a-pole-1", "a-pole-2", "a-pole-3");

        // Organization B, on A's order: whatever the rules allow its administrator, the selection
        // is confined to the order's organization and to the caller's tenant, so nothing matches.
        var byBOnAsOrder = await PostAsync(asB, "service-orders/ord-a/targets/selection/preview", FenceBody());
        byBOnAsOrder.GetProperty("matched").GetInt32().Should().Be(0);

        // Organization B, on its own order and on the map: only its own assets.
        var fenceB = await PostAsync(asB, "service-orders/ord-b/targets/selection/apply", FenceBody());
        var resolvedByB = await PostAsync(asB, "selection/resolve", new { strategyId = "polygon-fence", parameters = new { rings = Fence } });
        var resolvedByA = await PostAsync(asA, "selection/resolve", new { strategyId = "polygon-fence", parameters = new { rings = Fence } });

        fenceB.GetProperty("added").GetInt32().Should().Be(2);
        resolvedByB.GetProperty("featureIds").EnumerateArray().Select(v => v.GetString()).Should().Equal("b-pole-1", "b-pole-2");
        resolvedByA.GetProperty("featureIds").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("a-manhole-1", "a-pole-1", "a-pole-2", "a-pole-3");

        // What is stored agrees: no target of A's order points at an asset of B, and vice versa.
        var stored = await app.StoredTargetsAsync();
        stored.Where(t => string.Equals(t.OrderId, "ord-a", StringComparison.Ordinal)).Should().HaveCount(3)
            .And.OnlyContain(t => t.OrganizationId == OrgA && t.FeatureId.StartsWith("a-", StringComparison.Ordinal));
        stored.Where(t => string.Equals(t.OrderId, "ord-b", StringComparison.Ordinal)).Should().HaveCount(2)
            .And.OnlyContain(t => t.OrganizationId == OrgB && t.FeatureId.StartsWith("b-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsagesAndConflicts_AcrossTwoOrdersOfOneOrganization_AreReportedThroughTheRealStore()
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAssetsAsync();
        await app.SeedOrderAsync("ord-first", OrgA, AdminA, ServiceOrderStatus.Pending);
        await app.SeedOrderAsync("ord-second", OrgA, AdminA);
        using var client = app.ClientFor("admin-a");

        await PostAsync(client, "service-orders/ord-first/targets/selection/apply", FenceBody());
        var second = await PostAsync(client, "service-orders/ord-second/targets/selection/apply", FenceBody());
        var usages = await PostAsync(
            client, "service-orders/targets/usages", new { featureIds = new[] { "a-pole-1", "a-pole-2", "a-pole-far" }, excludingOrderId = "ord-second" });

        second.GetProperty("added").GetInt32().Should().Be(3);
        second.GetProperty("warningCount").GetInt32().Should().Be(3, "the order type warns on overlap and ord-first holds the same poles");
        usages.GetProperty("items").EnumerateArray().Select(u => (u.GetProperty("featureId").GetString(), u.GetProperty("orderId").GetString()))
            .Should().Equal(("a-pole-1", "ord-first"), ("a-pole-2", "ord-first"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object FenceBody(int pageSize = 50) =>
        new { strategyId = "polygon-fence", parameters = new { rings = Fence }, page = 1, pageSize };

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The host under test over a freshly migrated database.</summary>
    private sealed class TestApp(IHost host, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestApp> StartAsync()
        {
            var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
            {
                Database = $"target_selection_e2e_{Guid.NewGuid():N}",
            }.ConnectionString;

            await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
                await geoAssets.Database.MigrateAsync();
            await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
                await identity.Database.MigrateAsync();
            await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
                await serviceOrders.Database.MigrateAsync();

            var host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services => ConfigureServices(services, connectionString));
                    webHost.Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Headers.TryGetValue(TokenHeader, out var token))
                            {
                                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                                    [new Claim(ClaimTypes.NameIdentifier, token.ToString())], "TestScheme"));
                            }

                            await next();
                        });
                        app.UseRouting();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapServiceOrderTargetsApi());
                    });
                })
                .StartAsync();

            return new TestApp(host, connectionString);
        }

        /// <summary>The registrations of <c>GeoAssets.Server/Program.cs</c> that this feature runs on.</summary>
        private static void ConfigureServices(IServiceCollection services, string connectionString)
        {
            services.AddRouting();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new GeoAssetsActivitySource("e2e"));
            services.AddSingleton(new GeoAssetsMeter("e2e"));

            services.AddPostgresAssetSpatialQuery(connectionString);
            services.AddOrderTypeRegistry(registry => registry.Register(new OrderType
            {
                Id = PoleSurvey,
                DisplayName = "Pole survey",
                TargetPolicy = new OrderTargetPolicy
                {
                    TargetAssetTypeIds = ["pole"],
                    AliasAttributeKey = "plate",
                    Exclusivity = OrderTargetExclusivity.WarnOnOverlap,
                },
            }));
            services.AddWorkflowPersistence(o => o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
            services.AddServiceOrderRules();
            services.AddScoped<ServerWorkflowPrincipalFactory>();
            services.AddFeatureSelection();
            services.AddOrderTargets();
            services.AddObservableOrderTargetSelection();

            services.AddScoped<ITenantContext, RequestTenantContext>();
            services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
            services.AddAuthorization(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            services.AddGeoAuthorizationPolicyBridge();
            services.AddSingleton<IGeoAuthorizationService, TokenAuthorizationService>();
            services.AddSingleton<IOrganizationGrantRepository, NoGrantsRepository>();
        }

        public HttpClient ClientFor(string token)
        {
            var server = host.GetTestServer();
            var client = server.CreateClient();
            client.BaseAddress = new Uri(server.BaseAddress, "api/workflow/");
            client.DefaultRequestHeaders.Add(TokenHeader, token);
            return client;
        }

        /// <summary>
        /// Seeds through the real provider, so <c>geo_entity_attribute</c> is filled as in production.
        /// Inside the fence: three poles and a manhole of A, two poles of B. Outside: one pole of A.
        /// </summary>
        public async Task SeedAssetsAsync()
        {
            var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
            await using var provider = new PostgresAssetProvider(
                new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
            provider.AddRange(
            [
                Asset("a-pole-1", 1, 1, OrgA, "P-100"),
                Asset("a-pole-2", 2, 2, OrgA, "P-200"),
                Asset("a-pole-3", 3, 3, OrgA, "P-300"),
                Asset("a-manhole-1", 4, 4, OrgA, plate: null, assetTypeId: "manhole"),
                Asset("a-pole-far", 50, 50, OrgA, "P-900"),
                Asset("b-pole-1", 1.5, 1.5, OrgB, "P-100"),
                Asset("b-pole-2", 2.5, 2.5, OrgB, "P-700"),
            ]);
        }

        private static GeoFeature Asset(string id, double longitude, double latitude, Guid organizationId, string? plate, string assetTypeId = "pole")
        {
            var feature = new GeoFeature
            {
                Id = id,
                Geometry = new GeoPoint(longitude, latitude),
                Properties = new GeoFeatureProperties
                {
                    Name = id,
                    AssetTypeId = assetTypeId,
                    OrganizationId = organizationId,
                    CreatedAt = SeedTime,
                    UpdatedAt = SeedTime,
                },
            };
            if (plate is not null) feature.Properties.CustomAttributes["plate"] = plate;
            return feature;
        }

        public async Task SeedOrderAsync(string id, Guid organizationId, Guid createdBy, string status = ServiceOrderStatus.Draft)
        {
            await using var scope = host.Services.CreateAsyncScope();
            ((RequestTenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>()).Override = organizationId;

            await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder
            {
                Id = id,
                Title = id,
                OrderTypeId = PoleSurvey,
                Status = status,
                CreatedBy = createdBy.ToString(),
                OrganizationId = organizationId,
                CreatedAt = SeedTime,
            });
        }

        public async Task<ServiceOrder> LoadOrderAsync(string id, Guid organizationId)
        {
            await using var scope = host.Services.CreateAsyncScope();
            ((RequestTenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>()).Override = organizationId;

            return (ServiceOrder)(await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetByIdAsync(id))!;
        }

        /// <summary>Every stored target, read with raw SQL so no tenant filter stands between the test and the table.</summary>
        public async Task<List<(string OrderId, string FeatureId, Guid OrganizationId)>> StoredTargetsAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT "ServiceOrderId", "FeatureId", "OrganizationId" FROM "ServiceOrderTargets" ORDER BY "ServiceOrderId", "FeatureId" """;

            var rows = new List<(string, string, Guid)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetGuid(2)));
            return rows;
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// The request's organization, as <c>HttpTenantContext</c> resolves it in production; a
    /// seeding scope outside any request sets <see cref="Override"/> instead.
    /// </summary>
    private sealed class RequestTenantContext(IHttpContextAccessor httpContext) : ITenantContext
    {
        public Guid? Override { get; set; }

        public Guid CurrentOrganizationId =>
            Override
            ?? (httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } token && Identities.TryGetValue(token, out var identity)
                ? identity.OrganizationId
                : Guid.Empty);
    }

    private sealed class TokenAuthorizationService(IHttpContextAccessor httpContext) : IGeoAuthorizationService
    {
        private (Guid UserId, Guid OrganizationId) Current() =>
            httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } token && Identities.TryGetValue(token, out var identity)
                ? identity
                : throw new InvalidOperationException("No authenticated test identity on this request.");

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
        {
            _ = Current();
            return Task.FromResult(permissionCode is "serviceorders:read" or "features:read");
        }

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
        {
            var (userId, organizationId) = Current();
            return Task.FromResult(new AuthorizationContext
            {
                User = new AppUser { Id = userId, Email = "e2e@example.com", DisplayName = "E2E", OrganizationId = organizationId, CreatedAt = SeedTime },
                Roles = ["Administrator"],
                Claims = [],
                Permissions = ["serviceorders:read", "features:read"],
            });
        }
    }

    private sealed class NoGrantsRepository : IOrganizationGrantRepository
    {
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
