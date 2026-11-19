using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Server;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The global target ledger (XD01-379) end to end on the migrated shared-database schema, connected as the suite's
/// <b>non-owner</b> role so row-level security applies: <see cref="TargetLedgerService"/> and the real routes in a
/// <see cref="TestServer"/>, over a <c>DbContext</c> with the real <see cref="TenantSessionInterceptor"/> and an
/// <see cref="HttpTenantContext"/> whose caller is a service principal with <b>no organization</b> — what makes the
/// ledger rows visible is the <see cref="TenantOrganizationScope"/> the service enters before it touches the store.
/// Each fact gets a database of its own.
/// </summary>
public sealed class TargetLedgerEndToEndTests
{
    private const string RolesHeader = "X-Test-Roles";
    private const string ServiceCaller = "GeoAssets.ServiceCaller";
    private const string Base = "/api/targets/ledger";

    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");

    private static readonly GeoFeature[] Catalog =
    [
        .. new[] { "f1", "f2", "f3" }.Select(id => TargetLedgerServiceHarness.Feature(id, OrgX)),
        TargetLedgerServiceHarness.Feature("shared", OrgX),
        TargetLedgerServiceHarness.Feature("shared", OrgY),
    ];

    private sealed class ServiceCallerWithoutOrganization : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => new("svc-oid", string.Empty, "vertical service", [ServiceCaller], Guid.Empty);
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "svc"), new("azp", "vertical-service") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }

    private sealed class LedgerHost : IAsyncDisposable
    {
        private readonly IHost _host;

        private LedgerHost(IHost host, string ownerConnectionString)
        {
            _host = host;
            OwnerConnectionString = ownerConnectionString;
            Client = host.GetTestServer().CreateClient();
        }

        public string OwnerConnectionString { get; }

        public HttpClient Client { get; }

        public InMemoryOrderTypeDescriptorRegistry Descriptors => _host.Services.GetRequiredService<InMemoryOrderTypeDescriptorRegistry>();

        public static async Task<LedgerHost> StartAsync(bool connectAsRole = true)
        {
            var ownerConnectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
            {
                Database = $"target_ledger_e2e_{Guid.NewGuid():N}",
            }.ConnectionString;

            await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(ownerConnectionString)))
                await identity.Database.MigrateAsync();
            await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(ownerConnectionString)))
                await serviceOrders.Database.MigrateAsync();

            await using (var owner = new NpgsqlConnection(ownerConnectionString))
            {
                await owner.OpenAsync();
                await using var grant = owner.CreateCommand();
                grant.CommandText = $"""
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderTargets", "ServiceOrders", "WorkflowOutboxMessages", "WorkflowInboxProcessedMessages" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT ON "OrganizationGrants" TO {SecurityTestContainerFixture.TenantScopedRole};
                    """;
                await grant.ExecuteNonQueryAsync();
            }

            var appConnectionString = connectAsRole
                ? new NpgsqlConnectionStringBuilder(ownerConnectionString)
                {
                    Username = SecurityTestContainerFixture.TenantScopedRole,
                    Password = SecurityTestContainerFixture.TenantScopedRolePassword,
                }.ConnectionString
                : ownerConnectionString;

            var clock = new FakeTimeProvider(TargetLedgerServiceHarness.Now);
            var host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                        services.AddAuthorization();
                        services.AddSingleton<TimeProvider>(clock);
                        services.AddSingleton<IIntegrationMessageSerializer, ProtobufIntegrationMessageSerializer>();

                        services.AddScoped<ICurrentUserAccessor, ServiceCallerWithoutOrganization>();
                        services.AddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
                        services.AddScoped<ITenantContext>(sp => new HttpTenantContext(
                            sp.GetRequiredService<ICurrentUserAccessor>(), sp.GetRequiredService<ITenantOrganizationScope>()));
                        services.AddScoped<TenantSessionInterceptor>();
                        services.AddDbContext<ServiceOrderDbContext>((sp, options) => options
                            .UseNpgsql(appConnectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
                            .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));
                        services.AddTargetLedgerPersistence();
                        services.AddWorkflowOutboxStore();

                        var descriptors = new InMemoryOrderTypeDescriptorRegistry([]);
                        descriptors.Upsert("vertical-a", [Descriptor("vertical-a", "a-type", OrderTargetExclusivity.BlockOnOverlap, "Closed")]);
                        descriptors.Upsert("vertical-b", [Descriptor("vertical-b", "b-type", OrderTargetExclusivity.BlockOnOverlap, "Done")]);
                        services.AddSingleton(descriptors);
                        services.AddSingleton<IOrderTypeDescriptorRegistry>(descriptors);
                        services.AddSingleton(_ => new FeatureSelectionRegistry(clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly));
                        services.AddSingleton(sp => new BoundedFeatureSelector(sp.GetRequiredService<FeatureSelectionRegistry>(), new CatalogSpatialQuery(Catalog), null, clock));
                        services.AddScoped(sp => new TargetLedgerService(
                            TargetLedgerServiceHarness.CreateSelection(sp.GetRequiredService<BoundedFeatureSelector>(), clock),
                            sp.GetRequiredService<ITargetLedgerStore>(),
                            sp.GetRequiredService<IWorkflowUnitOfWork>(),
                            sp.GetRequiredService<IOutboxWriter>(),
                            sp.GetRequiredService<IIdempotentInbox>(),
                            sp.GetRequiredService<IOrderTypeDescriptorRegistry>(),
                            clock,
                            sp.GetRequiredService<ITenantOrganizationScope>()));
                    });
                    webHost.Configure(web =>
                    {
                        web.UseRouting();
                        web.UseAuthentication();
                        web.UseAuthorization();
                        web.UseEndpoints(endpoints => endpoints.MapTargetLedgerApi());
                    });
                })
                .StartAsync();

            return new LedgerHost(host, ownerConnectionString);
        }

        public AsyncServiceScope NewScope() => _host.Services.CreateAsyncScope();

        public async Task<List<string>> QueryAsync(string sql)
        {
            await using var db = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(OwnerConnectionString));
            return await db.Database.SqlQueryRaw<string>(sql).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
    }

    private static OrderTypeDescriptor Descriptor(string vertical, string type, OrderTargetExclusivity exclusivity, string terminal) => new()
    {
        VerticalId = vertical,
        OrderTypeId = type,
        DisplayNameKey = "k",
        ApiBaseRoute = "/r",
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity },
        TerminalStatuses = [terminal],
    };

    private static TargetLedgerOrder Order(LedgerHost host, string vertical, string type, string orderId, Guid org) =>
        new(vertical, orderId, type, org, host.Descriptors.GetAll().Single(d => string.Equals(d.VerticalId, vertical, StringComparison.Ordinal) && string.Equals(d.OrderTypeId, type, StringComparison.Ordinal)).TargetPolicy!);

    /// <summary>Runs <paramref name="work"/> as one request: a DI scope of its own, as an HTTP request or a consumed message gets.</summary>
    private static async Task<T> RequestAsync<T>(LedgerHost host, Func<TargetLedgerService, Task<T>> work)
    {
        await using var scope = host.NewScope();
        return await work(scope.ServiceProvider.GetRequiredService<TargetLedgerService>());
    }

    private static Task<TargetSelectionResult> ReserveAsync(LedgerHost host, TargetLedgerOrder order, params string[] features) =>
        RequestAsync(host, ledger => ledger.ReserveAsync(order, TargetLedgerServiceHarness.ByIds(order.OrderId, features), "tester", CancellationToken.None));

    private static bool HasSqlState(Exception exception, string sqlState) =>
        exception is PostgresException { SqlState: var state } && string.Equals(state, sqlState, StringComparison.Ordinal)
        || (exception.InnerException is { } inner && HasSqlState(inner, sqlState));

    // ── Service-level, as the non-owner role ──────────────────────────────────

    [Fact]
    public async Task ServiceCallerWithoutAnOrganization_ReserveUsagesCommitRelease_SucceedUnderRowLevelSecurity_ThroughTheOrganizationScope()
    {
        await using var host = await LedgerHost.StartAsync();
        var order = Order(host, "vertical-a", "a-type", "a1", OrgX);

        var reserved = await ReserveAsync(host, order, "f1", "f2");
        var page = await RequestAsync(host, ledger => ledger.GetPageAsync(order.Key, OrgX, 0, 10, CancellationToken.None));
        var committed = await RequestAsync(host, ledger => ledger.CommitAsync(order.Key, OrgX, null, "tester", CancellationToken.None));
        var released = await RequestAsync(host, ledger => ledger.ReleaseAsync(order.Key, OrgX, null, "tester", CancellationToken.None));

        reserved.Added.Should().Be(2);
        page.TotalCount.Should().Be(2);
        committed.Should().HaveCount(2).And.OnlyContain(t => t.Status == OrderTargetStatus.Done);
        released.Should().HaveCount(2);
        (await host.QueryAsync("""SELECT ("VerticalId" || ':' || "ServiceOrderId" || ':' || "FeatureId" || ':' || ("ReleasedAt" IS NOT NULL)::text)::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "FeatureId" """))
            .Should().Equal("vertical-a:a1:f1:true", "vertical-a:a1:f2:true");
        (await host.QueryAsync("""SELECT "MessageType"::text AS "Value" FROM "WorkflowOutboxMessages" ORDER BY "CreatedAtUtc", "MessageType" """))
            .Should().BeEquivalentTo(IntegrationEventTypes.TargetsReservedV1, IntegrationEventTypes.TargetsCommittedV1, IntegrationEventTypes.TargetsReleasedV1);
    }

    [Fact]
    public async Task AnotherOrganization_CannotSeeReleaseOrCommitTheOrdersTargets_UnderRowLevelSecurity()
    {
        await using var host = await LedgerHost.StartAsync();
        await ReserveAsync(host, Order(host, "vertical-a", "a-type", "a1", OrgX), "f1");
        var key = new TargetLedgerKey("vertical-a", "a1");

        var page = await RequestAsync(host, ledger => ledger.GetPageAsync(key, OrgY, 0, 10, CancellationToken.None));
        var release = await RequestAsync(host, ledger => ledger.ReleaseAsync(key, OrgY, null, "intruder", CancellationToken.None));
        var commit = await RequestAsync(host, ledger => ledger.CommitAsync(key, OrgY, null, "intruder", CancellationToken.None));

        page.TotalCount.Should().Be(0);
        release.Should().BeNull();
        commit.Should().BeNull();
        (await host.QueryAsync("""SELECT ("Status" || ':' || ("ReleasedAt" IS NULL)::text)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("Pending:true");
        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "WorkflowOutboxMessages" """)).Should().Equal("1");
    }

    [Fact]
    public async Task WithoutTheOrganizationScope_TheInsertIsRejectedByTheRowLevelSecurityWithCheck_AndWithItItSucceeds()
    {
        await using var host = await LedgerHost.StartAsync();
        var order = Order(host, "vertical-a", "a-type", "a1", OrgX);
        ServiceOrderTarget Target(string feature) => new ServiceOrderTargetBuilder()
            .WithId(TargetLedger.TargetId("vertical-a", "a1", feature)).WithOrderId("a1").WithFeatureId(feature).WithOrganizationId(OrgX).Build() with { VerticalId = "vertical-a" };

        // The store called directly, no organization entered: the session names the empty organization and the policy refuses the row.
        await using (var unscoped = host.NewScope())
        {
            var store = unscoped.ServiceProvider.GetRequiredService<ITargetLedgerStore>();
            var act = async () => await store.ReserveAsync(order, [Target("f1")], CancellationToken.None);

            var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
            HasSqlState(thrown, PostgresErrorCodes.InsufficientPrivilege).Should().BeTrue("the row's organization is not the session's: {0}", thrown);
        }

        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("0");

        // The same call after the organization was entered, as TargetLedgerService does, goes through.
        await using (var scoped = host.NewScope())
        {
            scoped.ServiceProvider.GetRequiredService<ITenantOrganizationScope>().Enter(OrgX);
            var store = scoped.ServiceProvider.GetRequiredService<ITargetLedgerStore>();

            (await store.ReserveAsync(order, [Target("f1")], CancellationToken.None)).Should().ContainSingle();
        }

        (await host.QueryAsync("""SELECT "FeatureId"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("f1");
    }

    [Fact]
    public async Task ExclusivityAcrossVerticals_InOneOrganization_HoldsUnderRowLevelSecurity()
    {
        await using var host = await LedgerHost.StartAsync();
        await ReserveAsync(host, Order(host, "vertical-a", "a-type", "a1", OrgX), "f1", "f2");

        var act = () => ReserveAsync(host, Order(host, "vertical-b", "b-type", "b1", OrgX), "f1");

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        (conflict.FeatureId, conflict.VerticalId, conflict.OrderId).Should().Be(("f1", "vertical-a", "a1"));
        (await host.QueryAsync("""SELECT DISTINCT "VerticalId"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("vertical-a");
        (await host.QueryAsync("""SELECT "MessageType"::text AS "Value" FROM "WorkflowOutboxMessages" """)).Should().Equal(IntegrationEventTypes.TargetsReservedV1);
    }

    // ── The terminal-release consumer path ────────────────────────────────────

    private static OrderStateChangedEvent Terminal(Guid? organization, string order = "a1") =>
        new(order, "a-type", "Open", "Closed", "operator", TargetLedgerServiceHarness.Now, VerticalId: "vertical-a", IsTerminal: true, OrganizationId: organization);

    private static IntegrationMessageContext Context(Guid messageId) =>
        new IntegrationMessageContextBuilder().WithMessageId(messageId).WithMessageType(IntegrationEventTypes.OrderStateChangedV1).Build();

    [Fact]
    public async Task TerminalRelease_WithTheEventsOrganization_ReleasesOnce_AsTheNonOwnerRole()
    {
        await using var host = await LedgerHost.StartAsync();
        await ReserveAsync(host, Order(host, "vertical-a", "a-type", "a1", OrgX), "f1", "f2");
        var messageId = Guid.NewGuid();

        var first = await RequestAsync(host, ledger => ledger.ReleaseTerminalAsync(Terminal(OrgX), Context(messageId), CancellationToken.None));
        var second = await RequestAsync(host, ledger => ledger.ReleaseTerminalAsync(Terminal(OrgX), Context(messageId), CancellationToken.None));

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await host.QueryAsync("""SELECT ("ReleasedAt" IS NOT NULL)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("true", "true");
        (await host.QueryAsync("""SELECT "MessageType"::text AS "Value" FROM "WorkflowOutboxMessages" WHERE "MessageType" = 'geoassets.targets.targets-released.v1' """)).Should().ContainSingle();
        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "WorkflowInboxProcessedMessages" """)).Should().Equal("1");
    }

    [Fact]
    public async Task TerminalRelease_ForAnOrganizationThatDoesNotOwnTheOrder_ReleasesNothing()
    {
        await using var host = await LedgerHost.StartAsync();
        await ReserveAsync(host, Order(host, "vertical-a", "a-type", "a1", OrgX), "f1");

        var released = await RequestAsync(host, ledger => ledger.ReleaseTerminalAsync(Terminal(OrgY), Context(Guid.NewGuid()), CancellationToken.None));

        released.Should().BeFalse();
        (await host.QueryAsync("""SELECT ("ReleasedAt" IS NULL)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("true");
    }

    // ── Through the routes, as the non-owner role (AC1) ───────────────────────

    private static HttpRequestMessage Post(string path, object body, string roles = ServiceCaller) =>
        new(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body), Headers = { { RolesHeader, roles } } };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(LedgerHost host, HttpRequestMessage request)
    {
        using (request)
        {
            using var response = await host.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }
    }

    private static object ReserveBody(Guid org, string type, params string[] ids) => new
    {
        organizationId = org, orderTypeId = type, strategyId = "identifier-list", parameters = new { attributeKey = "id", values = ids },
    };

    [Fact]
    public async Task Routes_CrossVerticalExclusivity_VerticalBsExclusiveReserveOnAHeldFeatureIs409_AndWritesNothingForB()
    {
        await using var host = await LedgerHost.StartAsync();
        (await SendAsync(host, Post($"{Base}/vertical-a/a1/reserve", ReserveBody(OrgX, "a-type", "f1", "f2")))).Status.Should().Be(HttpStatusCode.OK);

        var (status, body) = await SendAsync(host, Post($"{Base}/vertical-b/b1/reserve", ReserveBody(OrgX, "b-type", "f1", "f3")));
        using var usages = new HttpRequestMessage(HttpMethod.Get, new Uri($"{Base}/vertical-b/b1/usages?organizationId={OrgX}", UriKind.Relative));
        usages.Headers.Add(RolesHeader, ServiceCaller);
        var (usagesStatus, _) = await SendAsync(host, usages);

        status.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("targets.conflict");
        var conflict = body.GetProperty("conflicts").EnumerateArray().Should().ContainSingle().Subject;
        conflict.GetProperty("featureId").GetString().Should().Be("f1");
        conflict.GetProperty("verticalId").GetString().Should().Be("vertical-a");
        usagesStatus.Should().Be(HttpStatusCode.NotFound, "nothing was written for vertical B");
        (await host.QueryAsync("""SELECT DISTINCT "VerticalId"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("vertical-a");
        (await host.QueryAsync("""SELECT "PartitionKey"::text AS "Value" FROM "WorkflowOutboxMessages" """)).Should().Equal(["vertical-a/a1"], "no event is staged for vertical B");
    }

    [Fact]
    public async Task Routes_ReserveReleaseCommitUsages_WorkForAServiceCallerWithNoOrganization_AndAnotherOrganizationGets404()
    {
        await using var host = await LedgerHost.StartAsync();
        await SendAsync(host, Post($"{Base}/vertical-a/a1/reserve", ReserveBody(OrgX, "a-type", "f1", "f2")));

        var (commitStatus, commit) = await SendAsync(host, Post($"{Base}/vertical-a/a1/commit", new { organizationId = OrgX }));
        var (otherStatus, other) = await SendAsync(host, Post($"{Base}/vertical-a/a1/release", new { organizationId = OrgY }));
        var (releaseStatus, release) = await SendAsync(host, Post($"{Base}/vertical-a/a1/release", new { organizationId = OrgX }));
        var (repeatStatus, repeat) = await SendAsync(host, Post($"{Base}/vertical-a/a1/release", new { organizationId = OrgX }));
        using var usages = new HttpRequestMessage(HttpMethod.Get, new Uri($"{Base}/vertical-a/a1/usages?organizationId={OrgX}&take=1", UriKind.Relative));
        usages.Headers.Add(RolesHeader, ServiceCaller);
        var (usagesStatus, usagesBody) = await SendAsync(host, usages);

        commitStatus.Should().Be(HttpStatusCode.OK);
        commit.GetProperty("changed").GetInt32().Should().Be(2);
        otherStatus.Should().Be(HttpStatusCode.NotFound);
        other.GetProperty("code").GetString().Should().Be("orders.not-found");
        releaseStatus.Should().Be(HttpStatusCode.OK);
        release.GetProperty("changed").GetInt32().Should().Be(2);
        repeatStatus.Should().Be(HttpStatusCode.OK);
        repeat.GetProperty("changed").GetInt32().Should().Be(0);
        usagesStatus.Should().Be(HttpStatusCode.OK);
        usagesBody.GetProperty("totalCount").GetInt32().Should().Be(2);
        usagesBody.GetProperty("items").GetArrayLength().Should().Be(1);
        (await host.QueryAsync("""SELECT "MessageType"::text AS "Value" FROM "WorkflowOutboxMessages" ORDER BY "CreatedAtUtc" """)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Routes_AUserTokenIsRefused_AndNothingIsWritten()
    {
        await using var host = await LedgerHost.StartAsync();

        var (status, _) = await SendAsync(host, Post($"{Base}/vertical-a/a1/reserve", ReserveBody(OrgX, "a-type", "f1"), roles: "Administrator"));

        status.Should().Be(HttpStatusCode.Forbidden);
        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("0");
    }

    [Fact]
    public async Task Routes_PreviewPersistsNothing_AsTheNonOwnerRole()
    {
        await using var host = await LedgerHost.StartAsync();

        var (status, body) = await SendAsync(host, Post($"{Base}/vertical-a/a1/reserve", new
        {
            organizationId = OrgX, orderTypeId = "a-type", strategyId = "identifier-list", preview = true, parameters = new { attributeKey = "id", values = new[] { "f1" } },
        }));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("matched").GetInt32().Should().Be(1);
        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("0");
        (await host.QueryAsync("""SELECT count(*)::text AS "Value" FROM "WorkflowOutboxMessages" """)).Should().Equal("0");
    }

    [Fact]
    public async Task Routes_ASameIdFeatureOfTwoOrganizations_ConflictsAcrossOrganizations_WhenTheDatabaseRoleIsNotSubjectToRowLevelSecurity()
    {
        await using var host = await LedgerHost.StartAsync(connectAsRole: false);
        await SendAsync(host, Post($"{Base}/vertical-a/a1/reserve", ReserveBody(OrgY, "a-type", "shared")));

        var (status, body) = await SendAsync(host, Post($"{Base}/vertical-b/b1/reserve", ReserveBody(OrgX, "b-type", "shared")));

        status.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("conflicts")[0].TryGetProperty("orderId", out var orderId).Should().BeFalse("another organization's holder is not named");
        orderId.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
