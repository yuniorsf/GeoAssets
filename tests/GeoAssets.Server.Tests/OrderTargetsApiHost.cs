using System.Security.Claims;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The real <c>MapServiceOrderTargetsApi()</c> in a <see cref="TestServer"/> (XD01-308), with the
/// production authorization pipeline (fallback policy + permission bridge), the real selection
/// service and strategies, and in-memory stores behind them.
///
/// A request is authenticated by its <c>X-Test-Token</c> header — this host's stand-in for a
/// bearer token. The token alone decides who the caller is (<see cref="Identities"/>); nothing
/// in a URL or body can.
/// </summary>
internal sealed class OrderTargetsApiHost : IAsyncDisposable
{
    public const string TokenHeader = "X-Test-Token";
    public const string PoleSurvey = "pole-survey";
    public const string LimitedSurvey = "limited-survey";
    public const string BlockingSurvey = "blocking-survey";

    public static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-0000000a0308");
    public static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-0000000b0308");
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Who each token is. Permissions gate the routes; roles feed the order rules.</summary>
    public static readonly IReadOnlyDictionary<string, TestIdentity> Identities = new Dictionary<string, TestIdentity>(StringComparer.Ordinal)
    {
        ["admin"] = new(new Guid("00000000-0000-4000-8000-00000000ad01"), OrgA, ["Administrator"], ["serviceorders:read", "features:read"]),
        ["creator"] = new(new Guid("00000000-0000-4000-8000-00000000c001"), OrgA, [], ["serviceorders:read", "features:read"]),
        ["reader"] = new(new Guid("00000000-0000-4000-8000-00000000ea01"), OrgA, [], ["serviceorders:read"]),
        ["outsider"] = new(new Guid("00000000-0000-4000-8000-0000000000b1"), OrgB, [], ["serviceorders:read", "features:read"]),
        ["admin-b"] = new(new Guid("00000000-0000-4000-8000-0000000000b2"), OrgB, ["Administrator"], ["serviceorders:read", "features:read"]),
        ["no-permission"] = new(new Guid("00000000-0000-4000-8000-000000000001"), OrgA, ["Administrator"], []),
    };

    private readonly IHost _host;

    private OrderTargetsApiHost(IHost host)
    {
        _host = host;
        Server = host.GetTestServer();
        Orders = host.Services.GetRequiredService<FakeServiceOrderRepository>();
        Store = host.Services.GetRequiredService<InMemoryServiceOrderTargetStore>();
        OrderTypes = host.Services.GetRequiredService<OrderTypeRegistry>();
        Assets = host.Services.GetRequiredService<TestAssetProvider>();
        Grants = host.Services.GetRequiredService<TestOrganizationGrantRepository>();
    }

    public TestServer Server { get; }
    public FakeServiceOrderRepository Orders { get; }
    public InMemoryServiceOrderTargetStore Store { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public TestAssetProvider Assets { get; }
    public TestOrganizationGrantRepository Grants { get; }

    public static async Task<OrderTargetsApiHost> StartAsync(
        Action<OrderTargetSelectionOptions>? configure = null,
        IAssetSpatialQuery? spatialQuery = null,
        Func<IServiceOrderRepository, IServiceOrderRepository>? decorateOrders = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => ConfigureServices(services, configure, spatialQuery, decorateOrders));
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

        return new OrderTargetsApiHost(host);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        Action<OrderTargetSelectionOptions>? configure,
        IAssetSpatialQuery? spatialQuery,
        Func<IServiceOrderRepository, IServiceOrderRepository>? decorateOrders)
    {
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

        services.AddOrderTypeRegistry(RegisterOrderTypes);
        services.AddSingleton<FakeServiceOrderRepository>();
        services.AddSingleton<IServiceOrderRepository>(sp =>
        {
            IServiceOrderRepository orders = sp.GetRequiredService<FakeServiceOrderRepository>();
            return decorateOrders is null ? orders : decorateOrders(orders);
        });
        services.AddSingleton(sp => new InMemoryServiceOrderTargetStore(
            sp.GetRequiredService<FakeServiceOrderRepository>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ITenantContext>()));
        services.AddSingleton<IServiceOrderTargetReader>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());
        services.AddSingleton<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());

        services.AddSingleton<TestAssetProvider>();
        services.AddSingleton<IAssetProvider>(sp => sp.GetRequiredService<TestAssetProvider>());
        if (spatialQuery is not null) services.AddSingleton(spatialQuery);

        services.AddServiceOrderRules();
        services.AddScoped<ServerWorkflowPrincipalFactory>();
        services.AddFeatureSelection();
        services.AddOrderTargets(configure);

        // The production pipeline: every endpoint needs an authenticated caller unless it says
        // otherwise (AddGeoAssetsAuthentication's fallback policy), and named policies resolve
        // to permission codes through the bridge.
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddGeoAuthorizationPolicyBridge();
        services.AddSingleton<IGeoAuthorizationService, TokenAuthorizationService>();
        services.AddSingleton<ITenantContext, TokenTenantContext>();
        services.AddSingleton<TestOrganizationGrantRepository>();
        services.AddSingleton<IOrganizationGrantRepository>(sp => sp.GetRequiredService<TestOrganizationGrantRepository>());
    }

    private static void RegisterOrderTypes(OrderTypeRegistry registry)
    {
        registry.Register(SurveyType(PoleSurvey, selectionLimit: null, OrderTargetExclusivity.None));
        registry.Register(SurveyType(LimitedSurvey, selectionLimit: 2, OrderTargetExclusivity.None));
        registry.Register(SurveyType(BlockingSurvey, selectionLimit: null, OrderTargetExclusivity.BlockOnOverlap));
    }

    private static OrderType SurveyType(string id, int? selectionLimit, OrderTargetExclusivity exclusivity) => new()
    {
        Id = id,
        DisplayName = id,
        TargetPolicy = new OrderTargetPolicy
        {
            TargetAssetTypeIds = ["pole"],
            AliasAttributeKey = "plate",
            SelectionLimit = selectionLimit,
            Exclusivity = exclusivity,
            Activities = [new OrderTargetActivity("inspect", "Inspect"), new OrderTargetActivity("replace", "Replace")],
        },
    };

    public HttpClient ClientFor(string? token)
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri(Server.BaseAddress, "api/workflow/");
        if (token is not null) client.DefaultRequestHeaders.Add(TokenHeader, token);
        return client;
    }

    public async Task<ServiceOrder> SeedOrderAsync(
        string id = "ord-1",
        string orderTypeId = PoleSurvey,
        string status = ServiceOrderStatus.Draft,
        Guid? organizationId = null,
        string createdBy = "someone-else")
    {
        var order = new ServiceOrder
        {
            Id = id,
            Title = id,
            OrderTypeId = orderTypeId,
            Status = status,
            CreatedBy = createdBy,
            OrganizationId = organizationId ?? OrgA,
            CreatedAt = Now.UtcDateTime.AddDays(-1),
        };
        await Orders.AddAsync(order);
        return order;
    }

    /// <summary>Stores <paramref name="count"/> targets <c>f-001…</c> for the order, positioned on a diagonal.</summary>
    public async Task<IReadOnlyList<ServiceOrderTarget>> SeedTargetsAsync(string orderId, int count, Guid? organizationId = null)
    {
        var targets = Enumerable.Range(1, count)
            .Select(i => new ServiceOrderTargetBuilder()
                .WithId($"{orderId}-t{i:000}")
                .WithOrderId(orderId)
                .WithFeatureId($"f-{i:000}")
                .WithAlias($"P-{i:000}")
                .WithPosition(-38.0 - i, -12.0 - i)
                .WithOrganizationId(organizationId ?? OrgA)
                .Build())
            .ToList();
        await Store.AddRangeAsync(orderId, targets, CancellationToken.None);
        return targets;
    }

    /// <summary>
    /// Three poles and a manhole of organization A and two poles of organization B, all inside
    /// the fence <see cref="FenceRings"/> draws; plus one pole of A outside it.
    /// </summary>
    public void SeedAssets()
    {
        Assets.Add(Asset("a-pole-1", 1, 1, OrgA, plate: "P-100"));
        Assets.Add(Asset("a-pole-2", 2, 2, OrgA, plate: "P-200"));
        Assets.Add(Asset("a-pole-3", 3, 3, OrgA, plate: "P-300"));
        Assets.Add(Asset("a-manhole-1", 4, 4, OrgA, assetTypeId: "manhole"));
        Assets.Add(Asset("a-pole-far", 50, 50, OrgA, plate: "P-900"));
        Assets.Add(Asset("b-pole-1", 1.5, 1.5, OrgB, plate: "P-100"));
        Assets.Add(Asset("b-pole-2", 2.5, 2.5, OrgB, plate: "P-700"));
    }

    /// <summary>A closed square from (0,0) to (10,10).</summary>
    public static double[][][] FenceRings() => [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]];

    public static GeoFeature Asset(string id, double longitude, double latitude, Guid organizationId, string assetTypeId = "pole", string? plate = null)
    {
        var feature = new GeoFeature
        {
            Id = id,
            Geometry = new GeoPoint(longitude, latitude),
            Properties = new GeoFeatureProperties { Name = id, AssetTypeId = assetTypeId, OrganizationId = organizationId },
        };
        if (plate is not null) feature.Properties.CustomAttributes["plate"] = plate;
        return feature;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    internal sealed record TestIdentity(Guid UserId, Guid OrganizationId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

    /// <summary>The organization of the current request's token, as <c>HttpTenantContext</c> resolves it in production.</summary>
    private sealed class TokenTenantContext(IHttpContextAccessor httpContext) : ITenantContext
    {
        public Guid CurrentOrganizationId =>
            httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } token
            && Identities.TryGetValue(token, out var identity)
                ? identity.OrganizationId
                : Guid.Empty;
    }

    /// <summary>Answers for whoever the current request's token names — the fake of the identity store behind a bearer token.</summary>
    private sealed class TokenAuthorizationService(IHttpContextAccessor httpContext) : IGeoAuthorizationService
    {
        private TestIdentity Current()
        {
            var token = httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return token is not null && Identities.TryGetValue(token, out var identity)
                ? identity
                : throw new InvalidOperationException("No authenticated test identity on this request.");
        }

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(Current().Permissions.Contains(permissionCode, StringComparer.Ordinal));

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
        {
            var identity = Current();
            return Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = identity.UserId,
                    Email = "test@example.com",
                    DisplayName = "Test",
                    OrganizationId = identity.OrganizationId,
                    CreatedAt = Now.UtcDateTime,
                },
                Roles = identity.Roles,
                Claims = [],
                Permissions = identity.Permissions,
            });
        }
    }
}
