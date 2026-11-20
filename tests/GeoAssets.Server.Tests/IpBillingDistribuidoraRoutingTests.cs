using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Routing coverage for the Distribuidora and lamp-wattage-chart endpoints
/// <c>IpBillingDistribuidoraRestApiExtensions</c> adds (XD01-301): CRUD, the paging contract
/// (defaults, cap, bounds), validation, and the host's authenticated-caller-only boundary —
/// same host bootstrap as <see cref="IpBillingReferenceCrudRoutingTests"/>.
/// </summary>
public sealed class IpBillingDistribuidoraRoutingTests
{
    private const string Base = "/api/ip-billing/distribuidoras";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class RecordingMunicipioRepository : IMunicipioRepository
    {
        public List<Municipio> Saved { get; } = [];

        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(m => m.Id == id));
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(m => string.Equals(m.IbgeCode, ibgeCode, StringComparison.Ordinal)));
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Municipio>>(Saved);
        public Task AddAsync(Municipio municipio, CancellationToken ct = default)
        {
            Saved.Add(municipio);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Municipio municipio, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed record Harness(
        TestServer Server,
        InMemoryDistribuidoraRepository Distribuidoras,
        InMemoryDistribuidoraLampWattageRepository Chart,
        RecordingMunicipioRepository Municipios) : IDisposable
    {
        public HttpClient Client()
        {
            var client = Server.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
            return client;
        }

        public void Dispose() => Server.Dispose();
    }

    private static async Task<(List<T> Items, int TotalCount, int Page, int PageSize)> ReadPageAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var items = root.GetProperty("items").Deserialize<List<T>>(WebJson)!;
        return (items, root.GetProperty("totalCount").GetInt32(), root.GetProperty("page").GetInt32(), root.GetProperty("pageSize").GetInt32());
    }

    private static async Task<Harness> BuildAsync()
    {
        var distribuidoras = new InMemoryDistribuidoraRepository();
        var chart = new InMemoryDistribuidoraLampWattageRepository();
        var municipios = new RecordingMunicipioRepository();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IDistribuidoraRepository>(distribuidoras);
                    services.AddSingleton<IDistribuidoraLampWattageRepository>(chart);
                    services.AddSingleton<IMunicipioRepository>(municipios);
                    services.AddSingleton<GeoAssets.IpBilling.GrantSync.IMunicipioGrantSyncQueue>(new RecordingMunicipioGrantSyncQueue());
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapIpBillingApi());
                });
            })
            .StartAsync();

        return new Harness(host.GetTestServer(), distribuidoras, chart, municipios);
    }

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static async Task<Distribuidora> SeedAsync(Harness harness, string name)
    {
        var distribuidora = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = name };
        await harness.Distribuidoras.AddAsync(distribuidora);
        return distribuidora;
    }

    // ── Authentication boundary ───────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("GET", "/11111111-1111-1111-1111-111111111111")]
    [InlineData("PUT", "/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/11111111-1111-1111-1111-111111111111")]
    [InlineData("GET", "/11111111-1111-1111-1111-111111111111/lamp-wattages")]
    [InlineData("PUT", "/11111111-1111-1111-1111-111111111111/lamp-wattages")]
    [InlineData("DELETE", "/lamp-wattages/22222222-2222-2222-2222-222222222222")]
    public async Task EveryRoute_NoAuthenticatedPrincipal_Returns401(string method, string path)
    {
        using var harness = await BuildAsync();
        using var client = harness.Server.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), U(Base + path));
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Distribuidora CRUD ────────────────────────────────────────────────────

    [Fact]
    public async Task PostDistribuidora_Returns201_Persists_AndTrimsTheName()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var organizationId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(U(Base), new { organizationId, name = "  Enel SP " });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Distribuidora>();
        created!.Name.Should().Be("Enel SP");
        created.OrganizationId.Should().Be(organizationId);
        response.Headers.Location!.ToString().Should().Be($"{Base}/{created.Id}");
        (await harness.Distribuidoras.GetByIdAsync(created.Id)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostAndPutDistribuidora_BlankName_Returns400(string name)
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var existing = await SeedAsync(harness, "Enel SP");

        var post = await client.PostAsJsonAsync(U(Base), new { organizationId = Guid.NewGuid(), name });
        var put = await client.PutAsJsonAsync(U($"{Base}/{existing.Id}"), new { organizationId = Guid.NewGuid(), name });

        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await post.Content.ReadAsStringAsync()).Should().Contain("name");
        (await harness.Distribuidoras.GetByIdAsync(existing.Id))!.Name.Should().Be("Enel SP");
    }

    [Fact]
    public async Task GetDistribuidoraById_FoundAndMissing()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var existing = await SeedAsync(harness, "Enel SP");

        var found = await client.GetAsync(U($"{Base}/{existing.Id}"));
        var missing = await client.GetAsync(U($"{Base}/{Guid.NewGuid()}"));

        found.StatusCode.Should().Be(HttpStatusCode.OK);
        (await found.Content.ReadFromJsonAsync<Distribuidora>())!.Name.Should().Be("Enel SP");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutDistribuidora_Existing_Updates_AndMissing_Returns404()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var existing = await SeedAsync(harness, "Enel SP");
        var newOrganization = Guid.NewGuid();

        var updated = await client.PutAsJsonAsync(U($"{Base}/{existing.Id}"), new { organizationId = newOrganization, name = "Enel São Paulo" });
        var missing = await client.PutAsJsonAsync(U($"{Base}/{Guid.NewGuid()}"), new { organizationId = newOrganization, name = "Ghost" });

        updated.StatusCode.Should().Be(HttpStatusCode.NoContent);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stored = (await harness.Distribuidoras.GetByIdAsync(existing.Id))!;
        stored.Name.Should().Be("Enel São Paulo");
        stored.OrganizationId.Should().Be(newOrganization);
    }

    [Fact]
    public async Task DeleteDistribuidora_Returns204_AndIsIdempotent()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var existing = await SeedAsync(harness, "Enel SP");

        var first = await client.DeleteAsync(U($"{Base}/{existing.Id}"));
        var second = await client.DeleteAsync(U($"{Base}/{existing.Id}"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await harness.Distribuidoras.GetByIdAsync(existing.Id)).Should().BeNull();
    }

    // ── Paged list ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDistribuidoras_Pages_InNameOrder_WithTotalCount()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        foreach (var name in new[] { "Echo", "Alpha", "Delta", "Bravo", "Charlie" }) await SeedAsync(harness, name);

        var first = await ReadPageAsync<Distribuidora>(await client.GetAsync(U($"{Base}?page=1&pageSize=2")));
        var third = await ReadPageAsync<Distribuidora>(await client.GetAsync(U($"{Base}?page=3&pageSize=2")));

        first.Items.Select(d => d.Name).Should().Equal("Alpha", "Bravo");
        first.TotalCount.Should().Be(5);
        first.Page.Should().Be(1);
        first.PageSize.Should().Be(2);
        third.Items.Select(d => d.Name).Should().Equal("Echo");
        third.TotalCount.Should().Be(5);
        third.Page.Should().Be(3);
    }

    [Fact]
    public async Task GetDistribuidoras_NoPagingParameters_UsesPageOneAndTheDefaultPageSize()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();

        var page = await ReadPageAsync<Distribuidora>(await client.GetAsync(U(Base)));

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(IpBillingDistribuidoraRestApiExtensions.DefaultPageSize);
        harness.Distribuidoras.PageRequests.Should().Equal((1, 50));
    }

    [Fact]
    public async Task GetDistribuidoras_PageSizeAboveTheMaximum_IsCapped()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();

        var page = await ReadPageAsync<Distribuidora>(await client.GetAsync(U($"{Base}?page=2&pageSize=5000")));

        page.PageSize.Should().Be(IpBillingDistribuidoraRestApiExtensions.MaxPageSize);
        harness.Distribuidoras.PageRequests.Should().Equal((2, 100));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1&pageSize=10")]
    [InlineData("pageSize=0")]
    [InlineData("page=1&pageSize=-5")]
    public async Task PagedRoutes_PageOrPageSizeBelowOne_Return400_WithoutCallingTheRepository(string query)
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();

        var list = await client.GetAsync(U($"{Base}?{query}"));
        var chart = await client.GetAsync(U($"{Base}/{Guid.NewGuid()}/lamp-wattages?{query}"));

        list.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        chart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await list.Content.ReadAsStringAsync()).Should().Contain("pageSize");
        harness.Distribuidoras.PageRequests.Should().BeEmpty();
        harness.Chart.PageRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null, true, 1, 50)]
    [InlineData(3, 20, true, 3, 20)]
    [InlineData(1, 100, true, 1, 100)]
    [InlineData(1, 101, true, 1, 100)]
    [InlineData(0, 10, false, 0, 10)]
    [InlineData(1, 0, false, 1, 0)]
    public void TryResolvePaging_AppliesDefaultsCapAndBounds(int? page, int? pageSize, bool expectedOk, int expectedPage, int expectedPageSize)
    {
        var ok = IpBillingDistribuidoraRestApiExtensions.TryResolvePaging(page, pageSize, out var resolvedPage, out var resolvedPageSize);

        ok.Should().Be(expectedOk);
        resolvedPage.Should().Be(expectedPage);
        resolvedPageSize.Should().Be(expectedPageSize);
    }

    // ── Lamp wattage chart ────────────────────────────────────────────────────

    [Fact]
    public async Task PutLampWattage_Inserts_ThenUpdatesInPlaceKeepingTheId()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var enel = await SeedAsync(harness, "Enel SP");
        var url = U($"{Base}/{enel.Id}/lamp-wattages");

        var inserted = await client.PutAsJsonAsync(url, new { code = LampTypeCode.VS, wattageW = 100, ballastLossW = 17m });
        var updated = await client.PutAsJsonAsync(url, new { code = LampTypeCode.VS, wattageW = 100, ballastLossW = 21.5m });

        inserted.StatusCode.Should().Be(HttpStatusCode.OK);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = (await inserted.Content.ReadFromJsonAsync<DistribuidoraLampWattage>())!;
        var second = (await updated.Content.ReadFromJsonAsync<DistribuidoraLampWattage>())!;
        first.DistribuidoraId.Should().Be(enel.Id);
        second.Id.Should().Be(first.Id);
        second.BallastLossW.Should().Be(21.5m);
        second.TotalBilledW.Should().Be(121.5m);
        harness.Chart.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task PutLampWattage_UnknownDistribuidora_Returns404_AndStoresNothing()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();

        var response = await client.PutAsJsonAsync(
            U($"{Base}/{Guid.NewGuid()}/lamp-wattages"), new { code = LampTypeCode.VS, wattageW = 100, ballastLossW = 17m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        harness.Chart.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(99, 100, 17.0, "lamp type")]
    [InlineData(7, 0, 17.0, "wattageW")]
    [InlineData(7, -70, 17.0, "wattageW")]
    [InlineData(7, 100, -0.5, "ballastLossW")]
    public async Task PutLampWattage_InvalidRow_Returns400(int code, int wattageW, double ballastLossW, string expectedInMessage)
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var enel = await SeedAsync(harness, "Enel SP");

        var response = await client.PutAsJsonAsync(
            U($"{Base}/{enel.Id}/lamp-wattages"), new { code, wattageW, ballastLossW });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(expectedInMessage);
        harness.Chart.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLampWattages_Pages_SortedByCodeThenWattage_ScopedToTheDistribuidora()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var enel = await SeedAsync(harness, "Enel SP");
        var cpfl = await SeedAsync(harness, "CPFL");
        foreach (var (code, wattageW) in new[] { (LampTypeCode.VS, 250), (LampTypeCode.LD, 150), (LampTypeCode.VS, 70), (LampTypeCode.LD, 60) })
        {
            await harness.Chart.UpsertAsync(new DistribuidoraLampWattage { DistribuidoraId = enel.Id, Code = code, WattageW = wattageW });
        }

        await harness.Chart.UpsertAsync(new DistribuidoraLampWattage { DistribuidoraId = cpfl.Id, Code = LampTypeCode.FL, WattageW = 20 });

        var first = await ReadPageAsync<DistribuidoraLampWattage>(await client.GetAsync(U($"{Base}/{enel.Id}/lamp-wattages?page=1&pageSize=3")));
        var second = await ReadPageAsync<DistribuidoraLampWattage>(await client.GetAsync(U($"{Base}/{enel.Id}/lamp-wattages?page=2&pageSize=3")));
        var defaulted = await ReadPageAsync<DistribuidoraLampWattage>(await client.GetAsync(U($"{Base}/{enel.Id}/lamp-wattages?pageSize=999")));

        first.Items.Select(r => (r.Code, r.WattageW)).Should().Equal(
            (LampTypeCode.LD, 60), (LampTypeCode.LD, 150), (LampTypeCode.VS, 70));
        second.Items.Select(r => (r.Code, r.WattageW)).Should().Equal((LampTypeCode.VS, 250));
        first.TotalCount.Should().Be(4);
        second.TotalCount.Should().Be(4);
        defaulted.Page.Should().Be(1);
        defaulted.PageSize.Should().Be(100, "pageSize is capped");
    }

    [Fact]
    public async Task DeleteLampWattage_Returns204_RemovesTheRow_AndIsIdempotent()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var enel = await SeedAsync(harness, "Enel SP");
        var row = await harness.Chart.UpsertAsync(new DistribuidoraLampWattage { DistribuidoraId = enel.Id, Code = LampTypeCode.VS, WattageW = 100 });

        var first = await client.DeleteAsync(U($"{Base}/lamp-wattages/{row.Id}"));
        var second = await client.DeleteAsync(U($"{Base}/lamp-wattages/{row.Id}"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        harness.Chart.Rows.Should().BeEmpty();
    }

    // ── Município carries DistribuidoraId ─────────────────────────────────────

    [Fact]
    public async Task PostAndPutMunicipio_CarryDistribuidoraId_AndGetReturnsIt()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();
        var enel = await SeedAsync(harness, "Enel SP");
        object Body(Guid? distribuidoraId) => new
        {
            ibgeCode = "3550308",
            name = "São Paulo",
            uf = "SP",
            distribuidoraOrganizationId = Guid.NewGuid(),
            prefeituraOrganizationId = (Guid?)null,
            distribuidoraId,
        };

        var post = await client.PostAsJsonAsync(U("/api/ip-billing/municipios"), Body(enel.Id));
        post.StatusCode.Should().Be(HttpStatusCode.Created);
        var municipio = harness.Municipios.Saved.Should().ContainSingle().Which;
        municipio.DistribuidoraId.Should().Be(enel.Id);

        var get = await client.GetFromJsonAsync<Municipio>(U("/api/ip-billing/municipios/3550308"));
        get!.DistribuidoraId.Should().Be(enel.Id);

        var put = await client.PutAsJsonAsync(U($"/api/ip-billing/municipios/{municipio.Id}"), Body(null));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        municipio.DistribuidoraId.Should().BeNull();
    }

    [Fact]
    public async Task PostMunicipio_LegacyBodyWithoutDistribuidoraId_StillWorks()
    {
        using var harness = await BuildAsync();
        using var client = harness.Client();

        var post = await client.PostAsJsonAsync(U("/api/ip-billing/municipios"), new
        {
            ibgeCode = "3550308",
            name = "São Paulo",
            uf = "SP",
            distribuidoraOrganizationId = Guid.NewGuid(),
            prefeituraOrganizationId = (Guid?)null,
        });

        post.StatusCode.Should().Be(HttpStatusCode.Created);
        harness.Municipios.Saved.Single().DistribuidoraId.Should().BeNull();
    }
}
