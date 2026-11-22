using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-328's acceptance criteria over the real sync routes: a BIMBO technician holding "Contractor C1" on COELBA pulls
/// COELBA's "cut" orders within the grant's scope and none of COELBA's others, pushes an execution update to one, and is
/// answered "not found" for every COELBA order outside the entitlement; a BIMBO user without the scoped role gets nothing;
/// and revoking the grant or the assignment in Core removes the entitlement on the next sync cycle. The mirrors are filled
/// by the real sync workers from fake feeds, so "before" and "after" are each one worker cycle.
///
/// The decision is <c>ServiceOrderRules</c>' (XD01-326) over the mirrored grant and scoped role; row-level security of
/// the read path is covered on PostgreSQL by <c>FieldServiceMigrationChainTests.CrossOrganizationSync</c>.
/// </summary>
public sealed class FieldServiceCrossOrganizationSyncApiTests : IAsyncLifetime, IDisposable
{
    private static readonly Guid Coelba = Guid.Parse("c0e1ba00-0000-4000-8000-000000000328");
    private static readonly Guid Bimbo = Guid.Parse("b1b0b1b0-0000-4000-8000-000000000328");
    private static readonly Guid Stranger = Guid.Parse("57a77e70-0000-4000-8000-000000000328");
    private static readonly Guid TechnicianUserId = Guid.Parse("7ec70000-0000-4000-8000-000000000328");
    private static readonly Guid GrantId = Guid.Parse("67a70000-0000-4000-8000-000000000328");
    private const string Technician = "oid-bimbo-technician";
    private const string Colleague = "oid-bimbo-colleague";

    /// <summary>The attribute <see cref="ServiceOrder"/> reads its <see cref="IScopeableResource.ScopeId"/> from.</summary>
    private const string ScopeIdAttribute = "scopeId";
    private static readonly DateTime Seeded = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeGrantFeed _grantFeed = new();
    private readonly FakeScopedRoleFeed _scopedRoleFeed = new();
    private TestServer _server = null!;

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGrantFeed : IOrganizationGrantSyncClient
    {
        private readonly List<OrganizationGrantSyncEntry> _entries = [];

        public void Add(OrganizationGrantSyncEntry entry) => _entries.Add(entry with { SequenceNumber = _entries.Count + 1 });

        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrantSyncEntry>>([.. _entries.Where(e => e.SequenceNumber > afterSequence).Take(take)]);
    }

    private sealed class FakeScopedRoleFeed : IScopedRoleAssignmentSyncClient
    {
        private readonly List<ScopedRoleAssignmentSyncEntry> _entries = [];

        public void Add(ScopedRoleAssignmentSyncEntry entry) => _entries.Add(entry with { SequenceNumber = _entries.Count + 1 });

        public Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScopedRoleAssignmentSyncEntry>>([.. _entries.Where(e => e.SequenceNumber > afterSequence).Take(take)]);
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(ConfigureServices);
                webHost.Configure(ConfigureApp);
            })
            .StartAsync();
        _server = host.GetTestServer();

        await SeedOrderAsync("coelba-cut-in-scope", Coelba, "cut", "district-7", ServiceOrderStatus.InProgress);
        await SeedOrderAsync("coelba-cut-out-of-scope", Coelba, "cut", "district-9", ServiceOrderStatus.InProgress);
        await SeedOrderAsync("coelba-inspection", Coelba, "inspection", "district-7", ServiceOrderStatus.InProgress);
        await SeedOrderAsync("stranger-cut", Stranger, "cut", "district-7", ServiceOrderStatus.InProgress);
        await SeedOrderAsync("bimbo-own", Bimbo, "cut", null, ServiceOrderStatus.Pending);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = ["org_id"], RoleClaimTypes = ["roles"] }));
        services.AddScoped<ITenantContext, HttpTenantContext>();

        services.AddDbContext<ServiceOrderDbContext>(options => options
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, RowVersionDefaultModelCustomizer>());
        services.AddScoped<IServiceOrderRepository>(sp =>
            new ValidatingServiceOrderRepository(new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>())));
        services.AddSingleton<IGeoCoreClient>(new FakeGeoCoreClient());
        services.AddSingleton(TimeProvider.System);

        services.AddServiceOrderRules();
        services.AddScoped<IMirroredCrossOrganizationEntitlements, EFMirroredCrossOrganizationEntitlements>();
        services.AddFieldServiceCrossOrganizationAccess();
    }

    private static void ConfigureApp(IApplicationBuilder app)
    {
        using (var scope = app.ApplicationServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("""
                CREATE TRIGGER ServiceOrders_RowVersion_Update AFTER UPDATE ON ServiceOrders
                BEGIN
                    UPDATE ServiceOrders SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
                """);
        }

        app.Use(async (ctx, next) =>
        {
            var claims = new List<Claim>();
            if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var org)) claims.Add(new Claim("org_id", org.ToString()));
            if (ctx.Request.Headers.TryGetValue("X-Test-Oid", out var oid)) claims.Add(new Claim("oid", oid.ToString()));
            if (claims.Count > 0) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
            await next();
        });
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapFieldServiceSyncApi());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _server?.Dispose();
        _connection.Dispose();
    }

    // ── Arrange ──────────────────────────────────────────────────────────────

    private async Task SeedOrderAsync(string id, Guid organization, string orderTypeId, string? scopeId, string status)
    {
        using var scope = _server.Services.CreateScope();
        var order = new ServiceOrder
        {
            Id = id,
            Title = id,
            OrderTypeId = orderTypeId,
            CreatedBy = "seed",
            Status = status,
            OrganizationId = organization,
            CreatedAt = Seeded,
        };
        if (scopeId is not null) order.Attributes[ScopeIdAttribute] = scopeId;
        await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(order);
    }

    /// <summary>COELBA's grant to BIMBO: "cut" orders of district-7, usable only through a role held on COELBA.</summary>
    private static OrganizationGrantSyncEntry CoelbaGrant(bool isActive = true, bool requiresScopedRole = true, params string[] actions) => new()
    {
        SequenceNumber = 0,
        GrantId = GrantId,
        GranteeOrganizationId = Bimbo,
        ResourceOrganizationId = Coelba,
        ResourceType = "ServiceOrder",
        ScopeId = "district-7",
        IsActive = isActive,
        AllowedActions = actions.Length > 0 ? actions : ["serviceorders:view", "serviceorders:execute", "serviceorders:complete"],
        RequiresScopedRole = requiresScopedRole,
        OrderTypeIds = ["cut"],
    };

    /// <summary>The technician's "Contractor C1" role on COELBA, with these permission codes there.</summary>
    private static ScopedRoleAssignmentSyncEntry ContractorRole(bool isActive = true, params string[] codes) => new()
    {
        SequenceNumber = 0,
        UserId = TechnicianUserId,
        ExternalObjectId = Technician,
        ResourceOrganizationId = Coelba,
        RoleNames = isActive ? ["Contractor C1"] : [],
        PermissionCodes = isActive ? (codes.Length > 0 ? codes : ["serviceorders:view", "serviceorders:execute"]) : [],
        IsActive = isActive,
    };

    /// <summary>One cycle of each real sync worker over the fake feeds.</summary>
    private async Task SyncAsync()
    {
        var scopes = _server.Services.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new OrganizationGrantSyncOptions());
        using (var grants = new OrganizationGrantSyncWorker(scopes, _grantFeed, TimeProvider.System, options, NullLogger<OrganizationGrantSyncWorker>.Instance))
            await grants.SyncNextBatchAsync();
        using var roles = new ScopedRoleAssignmentSyncWorker(scopes, _scopedRoleFeed, TimeProvider.System, options, NullLogger<ScopedRoleAssignmentSyncWorker>.Instance);
        await roles.SyncNextBatchAsync();
    }

    private Task EntitleTechnicianAsync()
    {
        _grantFeed.Add(CoelbaGrant());
        _scopedRoleFeed.Add(ContractorRole());
        return SyncAsync();
    }

    // ── Act ──────────────────────────────────────────────────────────────────

    private HttpClient Client(Guid organization, string oid)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", organization.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        return client;
    }

    private static readonly Uri PullAll = new($"/api/field-service/sync/service-orders?sinceUtc={Uri.EscapeDataString(DateTime.MinValue.ToString("O"))}", UriKind.Relative);
    private static readonly Uri PushUri = new("/api/field-service/sync/service-orders", UriKind.Relative);

    private async Task<List<ServiceOrder>> PullAsync(Guid organization, string oid)
    {
        using var client = Client(organization, oid);
        var response = await client.GetAsync(PullAll);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ServiceOrder>>())!;
    }

    private async Task<ServiceOrder> StoredAsync(string id)
    {
        using var scope = _server.Services.CreateScope();
        return (ServiceOrder)(await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetByIdAsync(id))!;
    }

    private async Task<PushServiceOrderResult> PushAsync(Guid organization, string oid, ServiceOrder order, string idempotencyKey)
    {
        using var client = Client(organization, oid);
        var response = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(order, idempotencyKey) });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Single();
    }

    /// <summary>An execution update — a field recorded, the status left as it is.</summary>
    private async Task<PushServiceOrderResult> PushExecutionUpdateAsync(string id, string oid = Technician, string key = "exec-1")
    {
        var copy = await StoredAsync(id);
        copy.Attributes["meterReading"] = "4711";
        return await PushAsync(Bimbo, oid, copy, key);
    }

    private static bool NotFound(PushServiceOrderResult result) =>
        !result.Success && string.Equals(result.Error, "ServiceOrder not found.", StringComparison.Ordinal);

    // ── Pull ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_TheEntitledTechnician_GetsItsOwnOrders_AndCoelbasCutOrdersInTheGrantsScope_EachWithItsOrganization()
    {
        await EntitleTechnicianAsync();

        var orders = await PullAsync(Bimbo, Technician);

        orders.Select(o => o.Id).Should().BeEquivalentTo("bimbo-own", "coelba-cut-in-scope");
        orders.Single(o => string.Equals(o.Id, "coelba-cut-in-scope", StringComparison.Ordinal)).OrganizationId.Should().Be(Coelba);
        orders.Single(o => string.Equals(o.Id, "bimbo-own", StringComparison.Ordinal)).OrganizationId.Should().Be(Bimbo);
    }

    [Fact]
    public async Task Pull_ABimboUserWithoutTheScopedRole_GetsNoCoelbaOrder()
    {
        await EntitleTechnicianAsync();

        (await PullAsync(Bimbo, Colleague)).Select(o => o.Id).Should().Equal("bimbo-own");
    }

    [Fact]
    public async Task Pull_TheScopedRoleWithoutTheViewPermission_GetsNoCoelbaOrder()
    {
        _grantFeed.Add(CoelbaGrant());
        _scopedRoleFeed.Add(ContractorRole(isActive: true, "serviceorders:execute"));
        await SyncAsync();

        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Equal("bimbo-own");
    }

    [Fact]
    public async Task Pull_BeforeAndAfterTheGrantIsRevokedInCore_TheNextSyncCycleRemovesTheEntitlement()
    {
        await EntitleTechnicianAsync();
        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Contain("coelba-cut-in-scope");

        _grantFeed.Add(CoelbaGrant(isActive: false));
        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Contain("coelba-cut-in-scope", "the mirror has not been updated yet");
        await SyncAsync();

        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Equal("bimbo-own");
        NotFound(await PushExecutionUpdateAsync("coelba-cut-in-scope")).Should().BeTrue();
    }

    [Fact]
    public async Task Pull_BeforeAndAfterTheAssignmentIsRevokedInCore_TheNextSyncCycleRemovesTheEntitlement()
    {
        await EntitleTechnicianAsync();
        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Contain("coelba-cut-in-scope");

        _scopedRoleFeed.Add(ContractorRole(isActive: false));
        await SyncAsync();

        (await PullAsync(Bimbo, Technician)).Select(o => o.Id).Should().Equal("bimbo-own");
        NotFound(await PushExecutionUpdateAsync("coelba-cut-in-scope")).Should().BeTrue();
    }

    [Fact]
    public async Task Pull_AnOrganizationWideGrant_OpensTheCutOrdersInScopeToEveryBimboUser_AsTheRuleDecidesIt()
    {
        _grantFeed.Add(CoelbaGrant(isActive: true, requiresScopedRole: false, "serviceorders:view"));
        await SyncAsync();

        (await PullAsync(Bimbo, Colleague)).Select(o => o.Id).Should().BeEquivalentTo("bimbo-own", "coelba-cut-in-scope");
        NotFound(await PushExecutionUpdateAsync("coelba-cut-in-scope", Colleague)).Should().BeTrue("the grant lists no execute action");
    }

    [Fact]
    public async Task Pull_AStrangerOrganization_SeesNoneOfIt()
    {
        await EntitleTechnicianAsync();

        using var client = Client(Stranger, Technician);
        var orders = (await (await client.GetAsync(PullAll)).Content.ReadFromJsonAsync<List<ServiceOrder>>())!;

        orders.Select(o => o.Id).Should().Equal("stranger-cut");
    }

    // ── Push ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Push_TheEntitledTechnicianPushesAnExecutionUpdate_ItIsApplied()
    {
        await EntitleTechnicianAsync();

        var result = await PushExecutionUpdateAsync("coelba-cut-in-scope");

        result.Success.Should().BeTrue(result.Error);
        var stored = await StoredAsync("coelba-cut-in-scope");
        stored.Attributes["meterReading"].Should().Be("4711");
        stored.OrganizationId.Should().Be(Coelba, "a push never moves an order to another organization");
    }

    [Theory]
    [InlineData("coelba-cut-out-of-scope")]
    [InlineData("coelba-inspection")]
    [InlineData("stranger-cut")]
    public async Task Push_ACoelbaOrderOutsideTheEntitlement_IsReportedAsNotFound_AndNotApplied(string id)
    {
        await EntitleTechnicianAsync();

        NotFound(await PushExecutionUpdateAsync(id)).Should().BeTrue();
        (await StoredAsync(id)).Attributes.Should().NotContainKey("meterReading");
    }

    [Fact]
    public async Task Push_ABimboUserWithoutTheScopedRole_IsReportedAsNotFound()
    {
        await EntitleTechnicianAsync();

        NotFound(await PushExecutionUpdateAsync("coelba-cut-in-scope", Colleague)).Should().BeTrue();
    }

    [Fact]
    public async Task Push_ACompletion_NeedsTheCompletePermissionOnCoelba_AndIsAppliedOnceTheMirrorCarriesIt()
    {
        await EntitleTechnicianAsync();
        var completion = await StoredAsync("coelba-cut-in-scope");
        completion.Status = ServiceOrderStatus.Completed;

        NotFound(await PushAsync(Bimbo, Technician, completion, "complete-1")).Should().BeTrue("the role held on COELBA carries no serviceorders:complete");

        _scopedRoleFeed.Add(ContractorRole(isActive: true, "serviceorders:view", "serviceorders:execute", "serviceorders:complete"));
        await SyncAsync();

        (await PushAsync(Bimbo, Technician, completion, "complete-2")).Success.Should().BeTrue();
        (await StoredAsync("coelba-cut-in-scope")).Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Theory]
    [InlineData("orderType")]
    [InlineData("scope")]
    public async Task Push_ThatWouldCarryTheOrderOutOfTheGrant_IsReportedAsNotFound(string change)
    {
        await EntitleTechnicianAsync();
        var copy = await StoredAsync("coelba-cut-in-scope");
        if (string.Equals(change, "orderType", StringComparison.Ordinal)) copy.OrderTypeId = "inspection";
        else copy.Attributes[ScopeIdAttribute] = "district-9";

        NotFound(await PushAsync(Bimbo, Technician, copy, "escape-1")).Should().BeTrue();
        var stored = await StoredAsync("coelba-cut-in-scope");
        stored.OrderTypeId.Should().Be("cut");
        stored.Attributes[ScopeIdAttribute].Should().Be("district-7");
    }

    [Fact]
    public async Task Push_TheSameIdempotencyKeyTwice_IsDedupedForACrossOrganizationOrderToo()
    {
        await EntitleTechnicianAsync();

        (await PushExecutionUpdateAsync("coelba-cut-in-scope", key: "replay-1")).Success.Should().BeTrue();
        var replay = await PushExecutionUpdateAsync("coelba-cut-in-scope", key: "replay-1");

        replay.Success.Should().BeTrue();
        replay.Duplicate.Should().BeTrue();
    }

    [Fact]
    public async Task Push_TheTechniciansOwnOrder_IsUnchanged_WithOrWithoutAnyEntitlement()
    {
        var own = await StoredAsync("bimbo-own");
        own.Status = ServiceOrderStatus.InProgress;
        (await PushAsync(Bimbo, Colleague, own, "own-1")).Success.Should().BeTrue();

        await EntitleTechnicianAsync();
        var again = await StoredAsync("bimbo-own");
        again.Attributes["note"] = "done";
        (await PushAsync(Bimbo, Technician, again, "own-2")).Success.Should().BeTrue();
    }
}
