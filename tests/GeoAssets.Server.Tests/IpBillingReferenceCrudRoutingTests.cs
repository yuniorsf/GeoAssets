using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Integration coverage for <c>IpBillingRestApiExtensions.MapIpBillingApi</c>'s new
/// Município/UnidadeOrganizacional write endpoints (XD01-177) — CRUD plus the default
/// "every endpoint requires an authenticated caller" boundary, mirroring
/// <c>IspModuleRoutingTests</c>'s host bootstrap (GeoAssets.FieldService.Server.Tests) since
/// this host now closes the same authentication gap that one already did.
/// </summary>
public sealed class IpBillingReferenceCrudRoutingTests
{
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class InMemoryMunicipioRepository : IMunicipioRepository
    {
        private readonly Dictionary<Guid, Municipio> _byId = [];

        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) =>
            Task.FromResult(_byId.Values.FirstOrDefault(m => string.Equals(m.IbgeCode, ibgeCode, StringComparison.Ordinal)));

        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Municipio>>(_byId.Values.ToList());

        public Task AddAsync(Municipio municipio, CancellationToken ct = default)
        {
            _byId[municipio.Id] = municipio;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Municipio municipio, CancellationToken ct = default)
        {
            _byId[municipio.Id] = municipio;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _byId.Remove(id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryUnidadeOrganizacionalRepository : IUnidadeOrganizacionalRepository
    {
        private readonly Dictionary<Guid, UnidadeOrganizacional> _byId = [];

        public Task<UnidadeOrganizacional?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<IReadOnlyList<UnidadeOrganizacional>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UnidadeOrganizacional>>(_byId.Values.Where(u => u.MunicipioId == municipioId).ToList());

        public Task AddAsync(UnidadeOrganizacional unidade, CancellationToken ct = default)
        {
            _byId[unidade.Id] = unidade;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UnidadeOrganizacional unidade, CancellationToken ct = default)
        {
            _byId[unidade.Id] = unidade;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _byId.Remove(id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NeverCalledLampTypeRepository : ILampTypeRepository
    {
        public Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NeverCalledFaturamentoRepository : IFaturamentoRepository
    {
        public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Faturamento faturamento, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Faturamento> PostOrderCalculationAsync(string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline, FaturamentoPostingMode mode, DateTimeOffset postedAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Faturamento> OverwriteManualEntryAsync(Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference, DateTimeOffset postedAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NeverCalledTransformerResolver : ITransformerResolver
    {
        public Task<Guid?> ResolveTransformerFeatureIdAsync(string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NeverCalledPoleResolver : IPoleResolver
    {
        public Task<IReadOnlyList<Guid>> ResolvePoleFeatureIdsAsync(Guid transformerFeatureId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static async Task<(TestServer Server, InMemoryMunicipioRepository Municipios, InMemoryUnidadeOrganizacionalRepository Unidades)> BuildServerAsync()
    {
        var municipioRepo = new InMemoryMunicipioRepository();
        var unidadeRepo = new InMemoryUnidadeOrganizacionalRepository();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IMunicipioRepository>(municipioRepo);
                    services.AddSingleton<IUnidadeOrganizacionalRepository>(unidadeRepo);
                    services.AddSingleton<ILampTypeRepository>(new NeverCalledLampTypeRepository());
                    services.AddSingleton<IFaturamentoRepository>(new NeverCalledFaturamentoRepository());
                    services.AddSingleton<ITransformerResolver>(new NeverCalledTransformerResolver());
                    services.AddSingleton<IPoleResolver>(new NeverCalledPoleResolver());
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

        return (host.GetTestServer(), municipioRepo, unidadeRepo);
    }

    private static HttpClient AuthenticatedClient(TestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    private static object MunicipioRequest(string ibgeCode = "3550308", Guid? prefeituraId = null) => new
    {
        ibgeCode,
        name = "São Paulo",
        uf = "SP",
        distribuidoraOrganizationId = Guid.NewGuid(),
        prefeituraOrganizationId = prefeituraId,
    };

    [Fact]
    public async Task GetMunicipios_NoAuthenticatedPrincipal_Returns401()
    {
        var (server, _, _) = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/api/ip-billing/municipios", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostMunicipio_NoAuthenticatedPrincipal_Returns401()
    {
        var (server, _, _) = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/ip-billing/municipios", UriKind.Relative), MunicipioRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostMunicipio_Authenticated_Returns201AndPersists()
    {
        var (server, municipios, _) = await BuildServerAsync();
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync(new Uri("/api/ip-billing/municipios", UriKind.Relative), MunicipioRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await municipios.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task PutMunicipio_NotFound_Returns404()
    {
        var (server, _, _) = await BuildServerAsync();
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/ip-billing/municipios/{Guid.NewGuid()}", UriKind.Relative), MunicipioRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutMunicipio_Existing_UpdatesFieldsAndReturns204()
    {
        var (server, municipios, _) = await BuildServerAsync();
        using var client = AuthenticatedClient(server);
        await client.PostAsJsonAsync(new Uri("/api/ip-billing/municipios", UriKind.Relative), MunicipioRequest());
        var created = (await municipios.GetAllAsync()).Single();
        var prefeituraId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/ip-billing/municipios/{created.Id}", UriKind.Relative), MunicipioRequest(prefeituraId: prefeituraId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await municipios.GetByIdAsync(created.Id))!.PrefeituraOrganizationId.Should().Be(prefeituraId);
    }

    [Fact]
    public async Task DeleteMunicipio_Existing_RemovesAndReturns204()
    {
        var (server, municipios, _) = await BuildServerAsync();
        using var client = AuthenticatedClient(server);
        await client.PostAsJsonAsync(new Uri("/api/ip-billing/municipios", UriKind.Relative), MunicipioRequest());
        var created = (await municipios.GetAllAsync()).Single();

        var response = await client.DeleteAsync(new Uri($"/api/ip-billing/municipios/{created.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await municipios.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PostUnidadeOrganizacional_Authenticated_Returns201AndPersists()
    {
        var (server, _, unidades) = await BuildServerAsync();
        using var client = AuthenticatedClient(server);
        var municipioId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/ip-billing/unidades-organizacionais", UriKind.Relative),
            new { municipioId, name = "Zona Norte", code = "ZN" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await unidades.GetByMunicipioAsync(municipioId)).Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteUnidadeOrganizacional_NoAuthenticatedPrincipal_Returns401()
    {
        var (server, _, _) = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.DeleteAsync(new Uri($"/api/ip-billing/unidades-organizacionais/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
