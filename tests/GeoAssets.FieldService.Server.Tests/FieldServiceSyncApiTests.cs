using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>FieldServiceSyncRestApiExtensions</c> (XD01-208's AC: "Covered by
/// integration tests"). Same SQLite-backed real-DI/HTTP pattern as
/// <see cref="ContractorUsLedgerApiTests"/> — the tenant-isolation mechanism under test is
/// <see cref="ServiceOrderDbContext"/>'s EF Core global query filter (pull) plus this endpoint's
/// own explicit <c>OrganizationId</c> check (push), not Row-Level Security.
///
/// Seeds ServiceOrders directly through <see cref="IServiceOrderRepository"/> resolved from the
/// test server's own DI container — this host deliberately maps no order-creation endpoint (see
/// <c>FieldServiceSyncRestApiExtensions</c>' own doc comment), so there's no HTTP route to seed
/// through.
/// </summary>
public sealed class FieldServiceSyncApiTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private static readonly Guid TenantA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync().ConfigureAwait(false);
        await BuildServerAsync().ConfigureAwait(false);
    }

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private async Task BuildServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());

                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUserAccessor>(sp =>
                        new ClaimsPrincipalCurrentUserAccessor(
                            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
                            new ClaimMapping { OrganizationIdClaimTypes = [OrganizationClaimType] }));
                    services.AddScoped<ITenantContext, HttpTenantContext>();

                    services.AddDbContext<ServiceOrderDbContext>(options => options
                        .UseSqlite(_connection)
                        .ReplaceService<IModelCustomizer, RowVersionDefaultModelCustomizer>());
                    services.AddScoped<IServiceOrderRepository>(sp =>
                        new ValidatingServiceOrderRepository(new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>())));
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
                        db.Database.EnsureCreated();

                        // SQLite has no native auto-updating rowversion column — this trigger is
                        // what makes ServiceOrders.RowVersion actually change on every UPDATE, so
                        // Push_StaleRowVersion_ReturnsConflictResult exercises a real optimistic-
                        // concurrency conflict (mirrors SqliteFixture in GeoAssets.Workflow.EFCore.Tests;
                        // not reused directly since this test project has no reference to that one).
                        db.Database.ExecuteSqlRaw("""
                            CREATE TRIGGER ServiceOrders_RowVersion_Update AFTER UPDATE ON ServiceOrders
                            BEGIN
                                UPDATE ServiceOrders SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                            END;
                            """);
                    }

                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var orgId))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(OrganizationClaimType, orgId.ToString())], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapFieldServiceSyncApi());
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
    }

    /// <summary>Seeds one ServiceOrder directly through the repository (bypassing HTTP — see class doc comment) under the given tenant, with the given CreatedAt/UpdatedAt.</summary>
    private async Task<ServiceOrder> SeedAsync(Guid organizationId, string id, DateTime createdAt, DateTime? updatedAt = null, string status = ServiceOrderStatus.Draft)
    {
        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();

        var order = new ServiceOrder
        {
            Id = id,
            Title = $"Order {id}",
            OrderTypeId = "type-a",
            CreatedBy = "tester",
            Status = status,
            OrganizationId = organizationId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
        await repo.AddAsync(order).ConfigureAwait(false);
        return order;
    }

    private HttpClient CreateClient(Guid tenant)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", tenant.ToString());
        return client;
    }

    private static Uri PullUri(DateTime sinceUtc) =>
        new($"/api/field-service/sync/service-orders?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}", UriKind.Relative);

    private static readonly Uri PushUri = new("/api/field-service/sync/service-orders", UriKind.Relative);

    [Fact]
    public async Task Pull_ReturnsOrdersModifiedSinceTimestamp_ScopedToOwnTenant()
    {
        await SeedAsync(TenantA, "old-order", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(TenantA, "new-order", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(TenantB, "other-tenant-order", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        using var client = CreateClient(TenantA);
        var response = await client.GetAsync(PullUri(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await response.Content.ReadFromJsonAsync<List<ServiceOrder>>();
        orders!.Select(o => o.Id).Should().BeEquivalentTo(["new-order"]);
    }

    [Fact]
    public async Task Pull_IncludesOrderUpdatedSinceEvenIfCreatedEarlier()
    {
        await SeedAsync(TenantA, "edited-order",
            createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            updatedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        using var client = CreateClient(TenantA);
        var response = await client.GetAsync(PullUri(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var orders = await response.Content.ReadFromJsonAsync<List<ServiceOrder>>();

        orders!.Should().ContainSingle(o => o.Id == "edited-order");
    }

    [Fact]
    public async Task Push_UpdateExistingOwnOrder_Succeeds()
    {
        var seeded = await SeedAsync(TenantA, "order-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        seeded.Status = ServiceOrderStatus.Pending; // valid transition from the seeded Draft default

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(seeded, "idem-1") });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();
        results!.Should().ContainSingle(r => r.Id == "order-1" && r.Success && !r.Duplicate);
    }

    [Fact]
    public async Task Push_UnknownOrderId_ReturnsFailureResult()
    {
        var phantom = new ServiceOrder { Id = "does-not-exist", Title = "x", OrderTypeId = "type-a", CreatedBy = "tester", OrganizationId = TenantA };

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(phantom, "idem-1") });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();
        results!.Should().ContainSingle(r => r.Id == "does-not-exist" && !r.Success);
    }

    [Fact]
    public async Task Push_DifferentTenantsOrder_FailsAndDoesNotMutate()
    {
        var seeded = await SeedAsync(TenantA, "order-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var tampered = new ServiceOrder
        {
            Id = seeded.Id,
            Title = "TAMPERED",
            OrderTypeId = seeded.OrderTypeId,
            CreatedBy = seeded.CreatedBy,
            OrganizationId = seeded.OrganizationId,
            RowVersion = seeded.RowVersion,
        };

        using var attackerClient = CreateClient(TenantB);
        var response = await attackerClient.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(tampered, "idem-1") });
        var results = await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();

        results!.Should().ContainSingle(r => r.Id == "order-1" && !r.Success,
            "a cross-tenant push must fail, and look like 'not found' rather than disclosing that the order exists under another tenant");

        using var ownerClient = CreateClient(TenantA);
        var pullResponse = await ownerClient.GetAsync(PullUri(DateTime.MinValue));
        var orders = await pullResponse.Content.ReadFromJsonAsync<List<ServiceOrder>>();
        orders!.Single(o => string.Equals(o.Id, "order-1", StringComparison.Ordinal)).Title.Should().NotBe("TAMPERED");
    }

    [Fact]
    public async Task Push_StaleRowVersion_ReturnsConflictResult()
    {
        // Seeded as Pending so the first writer's Pending -> InProgress transition below is valid.
        await SeedAsync(TenantA, "order-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), status: ServiceOrderStatus.Pending);

        ServiceOrder staleClientCopy;
        using (var scope = _server.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();

            // Simulates the mobile client's last pull, capturing the RowVersion token as of then.
            staleClientCopy = (ServiceOrder)(await repo.GetByIdAsync("order-1"))!;

            // A second writer commits a change first, advancing RowVersion server-side.
            var firstWriterCopy = (ServiceOrder)(await repo.GetByIdAsync("order-1"))!;
            firstWriterCopy.Status = ServiceOrderStatus.InProgress;
            await repo.UpdateAsync(firstWriterCopy);
        }

        // The mobile client pushes its own change (a status valid from the server's current
        // InProgress state) using the now-stale RowVersion it last pulled.
        staleClientCopy.Status = ServiceOrderStatus.Completed;
        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(staleClientCopy, "idem-1") });
        var results = await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();

        results!.Should().ContainSingle(r => r.Id == "order-1" && !r.Success && r.Error!.Contains("Conflict", StringComparison.Ordinal));
    }

    /// <summary>
    /// XD01-236's AC: "A batch of queued offline actions replays against the server exactly
    /// once each (idempotency key dedup verified)." Simulates a reconnect burst replaying the
    /// exact same push (the client never saw the first attempt's ack): the second call with the
    /// same idempotency key must not re-apply the mutation (RowVersion stays what the first
    /// call's own successful write advanced it to) or spuriously conflict.
    /// </summary>
    [Fact]
    public async Task Push_SameIdempotencyKeyTwice_SecondCallIsDedupedAndFlaggedDuplicate()
    {
        var seeded = await SeedAsync(TenantA, "order-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        seeded.Status = ServiceOrderStatus.Pending;

        using var client = CreateClient(TenantA);
        var firstResponse = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(seeded, "idem-replay-1") });
        var firstResults = await firstResponse.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();
        firstResults!.Should().ContainSingle(r => r.Success && !r.Duplicate);

        // Client never saw the ack and replays the exact same payload/idempotency key.
        var secondResponse = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(seeded, "idem-replay-1") });
        var secondResults = await secondResponse.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResults!.Should().ContainSingle(r => r.Id == "order-1" && r.Success && r.Duplicate,
            "a replayed idempotency key must be recognized and short-circuited, not re-applied or rejected as a conflict");
    }

    /// <summary>XD01-236's AC: "Partial-batch failure doesn't corrupt local outbox state" — the
    /// server-side half of that guarantee: one item failing in a batch must not prevent the
    /// other item from being applied, and the response reports each independently.</summary>
    [Fact]
    public async Task Push_BatchWithOneFailingItem_OtherItemStillSucceeds()
    {
        var good = await SeedAsync(TenantA, "order-good", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        good.Status = ServiceOrderStatus.Pending;
        var phantom = new ServiceOrder { Id = "order-missing", Title = "x", OrderTypeId = "type-a", CreatedBy = "tester", OrganizationId = TenantA };

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem>
        {
            new(good, "idem-good"),
            new(phantom, "idem-missing"),
        });
        var results = await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>();

        results!.Should().HaveCount(2);
        results!.Should().ContainSingle(r => r.Id == "order-good" && r.Success);
        results!.Should().ContainSingle(r => r.Id == "order-missing" && !r.Success);
    }

    [Fact]
    public async Task Pull_NoAuthenticatedPrincipal_Returns401()
    {
        using var client = _server.CreateClient();

        var response = await client.GetAsync(PullUri(DateTime.MinValue));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _server.Dispose();
        _connection.Dispose();
    }
}
