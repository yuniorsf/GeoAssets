using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Contractors;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>ContractorUsLedgerRestApiExtensions</c> (XD01-207's AC:
/// "Endpoints pass integration tests for both authorized and cross-tenant-denied access").
///
/// Backed by a real in-memory SQLite <see cref="ServiceOrderDbContext"/> (same rationale as
/// <c>SqliteFixture</c> in GeoAssets.Workflow.EFCore.Tests) rather than a Postgres container —
/// the tenant-isolation mechanism this suite needs to prove at the REST layer is
/// <see cref="ServiceOrderDbContext"/>'s EF Core global query filter (already unit-tested in
/// <c>EFContractorUsLedgerRepositoryTests</c>) wired correctly through real DI/HTTP, not the
/// Row-Level-Security database backstop — that's <see cref="GeoAssets.Security.Tests"/>'ns job.
/// </summary>
public sealed class ContractorUsLedgerApiTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private static readonly Guid TenantA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

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

    private async Task<TestServer> BuildServerAsync()
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

                    services.AddSingleton(TimeProvider.System);
                    services.AddDbContext<ServiceOrderDbContext>(options => options.UseSqlite(_connection));
                    services.AddScoped<IContractorUsLedgerRepository, EFContractorUsLedgerRepository>();
                    // Needed for MapContractorUsLedgerApi's Espelho de Medição dossier endpoint
                    // (XD01-249) to resolve at all, even though none of this file's own tests
                    // call it — same whole-group lazy-metadata-building issue XD01-177 hit.
                    services.AddScoped<IServiceOrderRepository, EFServiceOrderRepository>();
                    services.AddScoped<IContractorUsPostingRepository, EFContractorUsPostingRepository>();
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                        scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>().Database.EnsureCreated();

                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var orgId))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(OrganizationClaimType, orgId.ToString())], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapContractorUsLedgerApi());
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
        return _server;
    }

    private TestServer _server = null!;

    private HttpClient CreateClient(Guid tenant)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", tenant.ToString());
        return client;
    }

    private static CreateContractorUsLedgerEntryRequest NewRequest(string usCode = "US-001") => new(
        ServiceOrderId: "order-1",
        UsCode: usCode,
        Description: "Poste trocado",
        Quantity: 2,
        UnitValue: 150.50m,
        PhotoProof: [new PhotoProofDto("https://example.com/p1.jpg", -46.633, -23.55, new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc))]);

    [Fact]
    public async Task Create_ThenGetById_RoundTripsForOwnTenant()
    {
        using var client = CreateClient(TenantA);

        var createResponse = await client.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();

        var getResponse = await client.GetAsync(new Uri($"/api/fieldservice/contractor-us-ledger/{created!.Id}", UriKind.Relative));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();

        fetched!.OrganizationId.Should().Be(TenantA);
        fetched.UsCode.Should().Be("US-001");
        fetched.TotalValue.Should().Be(301.00m);
        fetched.PhotoProof.Should().ContainSingle();
    }

    [Fact]
    public async Task GetById_DifferentTenant_Returns404()
    {
        using var ownerClient = CreateClient(TenantA);
        var created = await (await ownerClient.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest()))
            .Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();

        using var attackerClient = CreateClient(TenantB);
        var response = await attackerClient.GetAsync(new Uri($"/api/fieldservice/contractor-us-ledger/{created!.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a cross-tenant id must look identical to an unknown one");
    }

    [Fact]
    public async Task List_OnlyReturnsCallersOwnTenant()
    {
        using var clientA = CreateClient(TenantA);
        await clientA.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest("US-A"));

        using var clientB = CreateClient(TenantB);
        await clientB.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest("US-B"));

        var listResponse = await clientA.GetAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative));
        var entries = await listResponse.Content.ReadFromJsonAsync<List<ContractorUsLedgerEntryResponse>>();

        entries!.Should().ContainSingle(e => e.UsCode == "US-A");
        entries.Should().NotContain(e => e.UsCode == "US-B");
    }

    [Fact]
    public async Task Update_DifferentTenant_Returns404AndDoesNotMutate()
    {
        using var ownerClient = CreateClient(TenantA);
        var created = await (await ownerClient.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest()))
            .Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();

        using var attackerClient = CreateClient(TenantB);
        var updateRequest = new UpdateContractorUsLedgerEntryRequest("order-1", "TAMPERED", "Tampered", 99, 99, []);
        var updateResponse = await attackerClient.PutAsJsonAsync(
            new Uri($"/api/fieldservice/contractor-us-ledger/{created!.Id}", UriKind.Relative), updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var verify = await (await ownerClient.GetAsync(new Uri($"/api/fieldservice/contractor-us-ledger/{created.Id}", UriKind.Relative)))
            .Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();
        verify!.UsCode.Should().Be("US-001");
    }

    [Fact]
    public async Task Delete_DifferentTenant_IsNoOpAndEntryStillExists()
    {
        using var ownerClient = CreateClient(TenantA);
        var created = await (await ownerClient.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest()))
            .Content.ReadFromJsonAsync<ContractorUsLedgerEntryResponse>();

        using var attackerClient = CreateClient(TenantB);
        var deleteResponse = await attackerClient.DeleteAsync(new Uri($"/api/fieldservice/contractor-us-ledger/{created!.Id}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var verify = await ownerClient.GetAsync(new Uri($"/api/fieldservice/contractor-us-ledger/{created.Id}", UriKind.Relative));
        verify.StatusCode.Should().Be(HttpStatusCode.OK, "a different tenant's delete must be a no-op, not a real deletion");
    }

    [Fact]
    public async Task EspelhoDeMedicao_ReturnsEntriesAndSummedTotalForThatMonth()
    {
        using var client = CreateClient(TenantA);
        await client.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest("US-JAN"));

        // The endpoint stamps CreatedAt from the real TimeProvider.System at creation time, so
        // the statement month under test is "now", not a hardcoded calendar month.
        var now = TimeProvider.System.GetUtcNow();
        var response = await client.GetAsync(new Uri(
            $"/api/fieldservice/contractor-us-ledger/espelho-medicao?year={now.Year}&month={now.Month}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var espelho = await response.Content.ReadFromJsonAsync<EspelhoDeMedicaoResponse>();

        espelho!.Year.Should().Be(now.Year);
        espelho.Month.Should().Be(now.Month);
        espelho.Entries.Should().ContainSingle(e => e.UsCode == "US-JAN");
        espelho.TotalValue.Should().Be(301.00m);
    }

    [Fact]
    public async Task Create_NoAuthenticatedPrincipal_Returns401()
    {
        using var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/fieldservice/contractor-us-ledger", UriKind.Relative), NewRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // A full round-trip test seeding a ServiceOrder through this file's plain ServiceOrderDbContext
    // hits the same SQLite "RowVersion NOT NULL" gap GeoAssets.IpBilling.EFCore.Tests'
    // ServiceOrderSqliteTestDbContext already works around for its own fixture (XD01-180) — this
    // file doesn't use that workaround. The 404 test below still proves the endpoint is wired and
    // reachable through the real route group; EspelhoDeMedicaoDossierGeneratorTests (unit level)
    // already covers reproduction/idempotency/signing thoroughly without needing EF at all.
    [Fact]
    public async Task EspelhoDeMedicaoDossier_UnknownServiceOrder_Returns404()
    {
        using var client = CreateClient(TenantA);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/fieldservice/contractor-us-ledger/by-service-order/unknown-order/espelho-de-medicao-dossier", UriKind.Relative),
            new EspelhoDeMedicaoDossierRequest("test-tenant-secret"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
