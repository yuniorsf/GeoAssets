using System.Net;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-408 — <c>GET /api/workflow/service-orders</c> lists the orders a cross-organization grant reaches, through the real
/// route, authorization pipeline, principal factory and rules. The store behind it narrows the caller's list to its own
/// organization (as <c>ServiceOrderDbContext</c>'s tenant filter does) and answers the grant-aware read path for the
/// organizations asked (as <c>EFServiceOrderRepository.GetOfOtherOrganizationsAsync</c> does), so a granted order can only
/// reach the list through that path.
///
/// The scenario is XD01-324's: COELBA grants BIMBO <c>serviceorders:view</c>/<c>execute</c> on its "cut" orders in one
/// município, usable only through a role held on COELBA; a BIMBO technician holds "Contractor C1" on COELBA. No order is
/// dispatched to BIMBO.
/// </summary>
public sealed class GrantedServiceOrderListEndpointTests
{
    private const string TokenHeader = "X-Test-Token";
    private const string Read = "serviceorders:read";
    private const string View = "serviceorders:view";
    private const string Execute = "serviceorders:execute";
    private const string Scope = "municipio-123";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000408");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000408");
    private static readonly Guid Acme = new("acae0000-0000-4000-8000-000000000408");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Who each token is: the BIMBO technician, a COELBA member, and a member of an organization COELBA grants nothing.</summary>
    private static readonly Dictionary<string, AuthorizationContext> Identities = new(StringComparer.Ordinal)
    {
        ["technician"] = Context(Bimbo, "7ec40000-0000-4000-8000-000000000408", scopedOnCoelba: true),
        ["coelba"] = Context(Coelba, "c0e10000-0000-4000-8000-000000000408", scopedOnCoelba: false),
        ["acme"] = Context(Acme, "acae0001-0000-4000-8000-000000000408", scopedOnCoelba: false),
    };

    private static AuthorizationContext Context(Guid organizationId, string userId, bool scopedOnCoelba) => new()
    {
        User = new AppUser
        {
            Id = Guid.Parse(userId),
            Email = $"{userId}@example.test",
            DisplayName = userId,
            OrganizationId = organizationId,
            CreatedAt = Now.UtcDateTime.AddMonths(-1),
        },
        Roles = [],
        Claims = [],
        Permissions = [Read],
        ScopedRoles = scopedOnCoelba
            ? new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = ["Contractor C1"] }
            : new Dictionary<Guid, IReadOnlyList<string>>(),
        ScopedPermissions = scopedOnCoelba
            ? new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = [View, Execute] }
            : new Dictionary<Guid, IReadOnlyList<string>>(),
    };

    private static OrganizationGrant CutGrant(DateTime? expiresAt = null) => new()
    {
        GranteeOrganizationId = Bimbo,
        ResourceOrganizationId = Coelba,
        ResourceType = "ServiceOrder",
        ScopeId = Scope,
        AllowedActions = [View, Execute],
        RequiresScopedRole = true,
        OrderTypeIds = ["cut"],
        ExpiresAt = expiresAt,
        GrantedBy = "coelba-admin",
        GrantedAt = Now.UtcDateTime.AddDays(-7),
    };

    private static ServiceOrder Order(string id, Guid owner, string orderTypeId, string? scopeId, int daysAgo) => new()
    {
        Id = id,
        Title = id,
        OrderTypeId = orderTypeId,
        Status = ServiceOrderStatus.Pending,
        CreatedBy = "creator",
        OrganizationId = owner,
        CreatedAt = Now.UtcDateTime.AddDays(-daysAgo),
        Attributes = scopeId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = scopeId },
    };

    /// <summary>COELBA's orders of both types and two scopes, one order of BIMBO and one of ACME — none dispatched.</summary>
    private static IEnumerable<ServiceOrder> Orders() =>
    [
        Order("coelba-cut-old", Coelba, "cut", Scope, daysAgo: 5),
        Order("bimbo-own", Bimbo, "cut", scopeId: null, daysAgo: 4),
        Order("coelba-cut-other-scope", Coelba, "cut", "municipio-999", daysAgo: 3),
        Order("coelba-reconnect", Coelba, "reconnect", Scope, daysAgo: 2),
        Order("coelba-cut-new", Coelba, "cut", Scope, daysAgo: 1),
        Order("acme-own", Acme, "cut", Scope, daysAgo: 1),
    ];

    // ── The scenario ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheTechnician_SeesCoelbasCutOrdersInTheGrantsScope_AndNotItsOtherTypesOrScopes_WithoutAnyDispatch()
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant());

        var ids = await host.ListAsync("technician");

        ids.Should().Equal(["coelba-cut-old", "bimbo-own", "coelba-cut-new"], "own and granted orders, oldest first, as the store orders a list");
        host.Store.All.Should().OnlyContain(o => o.Dispatches.Count == 0);
    }

    [Fact]
    public async Task ACallerWithNoGrant_SeesOnlyItsOwnOrganizationsOrders()
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant());

        (await host.ListAsync("acme")).Should().Equal("acme-own");
        (await host.ListAsync("coelba")).Should().Equal(["coelba-cut-old", "coelba-cut-other-scope", "coelba-reconnect", "coelba-cut-new"]);
        host.Store.AskedFor.Should().BeEmpty("neither caller's organization holds a grant");
    }

    [Fact]
    public async Task WithoutTheScopedRole_TheGrantListsNothing()
    {
        await using var host = await Host.StartAsync(withScopedRole: false);
        host.Grants.Active.Add(CutGrant());

        (await host.ListAsync("technician")).Should().Equal("bimbo-own");
    }

    [Fact]
    public async Task ARevokedGrant_RemovesTheOrders_FromTheNextResponse()
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant());
        (await host.ListAsync("technician")).Should().Contain("coelba-cut-new");

        host.Grants.Active.Clear();

        (await host.ListAsync("technician")).Should().Equal("bimbo-own");
    }

    [Fact]
    public async Task AnExpiredGrant_RemovesTheOrders_FromTheNextResponse()
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant(expiresAt: Now.UtcDateTime.AddHours(1)));
        (await host.ListAsync("technician")).Should().Contain("coelba-cut-new");

        host.Clock.Advance(TimeSpan.FromHours(2));

        (await host.ListAsync("technician")).Should().Equal("bimbo-own");
    }

    // ── Parity with the by-id read ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("technician")]
    [InlineData("coelba")]
    [InlineData("acme")]
    public async Task TheListHoldsExactlyTheOrdersTheCallerCanReadById(string token)
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant());
        using var client = host.ClientFor(token);

        var readable = new List<string>();
        foreach (var order in host.Store.All)
        {
            var response = await client.GetAsync(Path($"service-orders/{order.Id}"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
            if (response.StatusCode == HttpStatusCode.OK) readable.Add(order.Id);
        }

        (await host.ListAsync(token)).Should().BeEquivalentTo(readable);
    }

    // ── What the read path is asked for ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheReadPath_IsAskedOnlyForTheOrganizationsTheCallersServiceOrderGrantsName()
    {
        await using var host = await Host.StartAsync();
        host.Grants.Active.Add(CutGrant());
        host.Grants.Active.Add(new OrganizationGrant
        {
            GranteeOrganizationId = Bimbo,
            ResourceOrganizationId = Acme,
            ResourceType = "Feature",
            AllowedActions = ["features:read"],
            GrantedBy = "acme-admin",
            GrantedAt = Now.UtcDateTime.AddDays(-1),
        });

        (await host.ListAsync("technician")).Should().NotContain("acme-own");

        host.Store.AskedFor.Should().Equal([Coelba], "a grant on another resource type opens no service order");
    }

    [Fact]
    public async Task AHostWithoutTheReadPath_ListsTheCallersOwnOrders_AsBefore()
    {
        await using var host = await Host.StartAsync(withReader: false);
        host.Grants.Active.Add(CutGrant());

        (await host.ListAsync("technician")).Should().Equal("bimbo-own");
        host.Store.AskedFor.Should().BeEmpty();
    }

    // ── The host ────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Host : IAsyncDisposable
    {
        private readonly IHost _host;

        private Host(IHost host)
        {
            _host = host;
            Store = host.Services.GetRequiredService<TenantFilteredOrders>();
            Grants = host.Services.GetRequiredService<TestOrganizationGrantRepository>();
            Clock = (FakeTimeProvider)host.Services.GetRequiredService<TimeProvider>();
        }

        public TenantFilteredOrders Store { get; }
        public TestOrganizationGrantRepository Grants { get; }
        public FakeTimeProvider Clock { get; }

        public static async Task<Host> StartAsync(bool withReader = true, bool withScopedRole = true)
        {
            var host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddHttpContextAccessor();
                        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
                        services.AddOrderTypeRegistry(registry =>
                        {
                            registry.Register(new OrderType { Id = "cut", DisplayName = "Cut" });
                            registry.Register(new OrderType { Id = "reconnect", DisplayName = "Reconnect" });
                        });
                        services.AddSingleton<IOrderTypeRepository, FakeOrderTypeRepository>();
                        services.AddSingleton(sp => new TenantFilteredOrders(sp.GetRequiredService<IHttpContextAccessor>(), Orders()));
                        services.AddSingleton<IServiceOrderRepository>(sp => sp.GetRequiredService<TenantFilteredOrders>());
                        if (withReader)
                            services.AddSingleton<ICrossOrganizationServiceOrderReader>(sp => sp.GetRequiredService<TenantFilteredOrders>());

                        services.AddServiceOrderRules();
                        services.AddScoped<ServerWorkflowPrincipalFactory>();
                        services.AddSingleton<TestOrganizationGrantRepository>();
                        services.AddSingleton<IOrganizationGrantRepository>(sp => sp.GetRequiredService<TestOrganizationGrantRepository>());
                        services.AddSingleton<IGeoAuthorizationService>(sp =>
                            new TokenAuthorizationService(sp.GetRequiredService<IHttpContextAccessor>(), withScopedRole));

                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                        services.AddAuthorization(options =>
                            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                        services.AddGeoAuthorizationPolicyBridge();
                    });
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
                        app.UseEndpoints(endpoints => endpoints.MapServiceOrdersApi());
                    });
                })
                .StartAsync();

            return new Host(host);
        }

        public HttpClient ClientFor(string token)
        {
            var server = _host.GetTestServer();
            var client = server.CreateClient();
            client.BaseAddress = new Uri(server.BaseAddress, "api/workflow/");
            client.DefaultRequestHeaders.Add(TokenHeader, token);
            return client;
        }

        public async Task<string[]> ListAsync(string token)
        {
            using var client = ClientFor(token);
            var body = await (await client.GetAsync(Path("service-orders"))).ReadJsonAsync();
            return [.. body.EnumerateArray().Select(o => o.GetProperty("id").GetString()!)];
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private static string? TokenOf(IHttpContextAccessor httpContext) =>
        httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Answers for whoever the request's token names; without <c>withScopedRole</c>, the technician holds nothing on COELBA.</summary>
    private sealed class TokenAuthorizationService(IHttpContextAccessor httpContext, bool withScopedRole) : IGeoAuthorizationService
    {
        private AuthorizationContext Current()
        {
            var token = TokenOf(httpContext) ?? throw new InvalidOperationException("No authenticated test identity on this request.");
            var context = Identities[token];
            return withScopedRole || !string.Equals(token, "technician", StringComparison.Ordinal)
                ? context
                : Context(Bimbo, context.User.Id.ToString(), scopedOnCoelba: false);
        }

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(Current().Permissions.Contains(permissionCode, StringComparer.Ordinal));

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(Current());
    }

    /// <summary>
    /// The store as EF Core holds it: lists narrowed to the caller's organization, a read by id that is not, and the grant-aware
    /// read path returning the orders of the organizations asked, the caller's own excluded. Records what that path is asked.
    /// </summary>
    private sealed class TenantFilteredOrders(IHttpContextAccessor httpContext, IEnumerable<ServiceOrder> orders)
        : IServiceOrderRepository, ICrossOrganizationServiceOrderReader
    {
        private static NotSupportedException Unused() => new("The list route must not call this.");

        public IReadOnlyList<ServiceOrder> All { get; } = [.. orders];

        public List<Guid> AskedFor { get; } = [];

        private Guid CallerOrganization =>
            TokenOf(httpContext) is { } token ? Identities[token].User.OrganizationId ?? Guid.Empty : Guid.Empty;

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. All.Where(o => o.OrganizationId == CallerOrganization)]);

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal)));

        public Task<IReadOnlyList<IServiceOrder>> GetOfOtherOrganizationsAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default)
        {
            AskedFor.AddRange(organizationIds);
            var own = CallerOrganization;
            return Task.FromResult<IReadOnlyList<IServiceOrder>>(
                [.. All.Where(o => o.OrganizationId != own && o.OrganizationId != Guid.Empty && organizationIds.Contains(o.OrganizationId))]);
        }

        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw Unused();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw Unused();
        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw Unused();
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw Unused();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw Unused();

        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }
}
