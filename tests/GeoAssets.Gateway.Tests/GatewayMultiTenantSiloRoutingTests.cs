using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// XD01-289's core AC, proven with two real provisioned tenant silos (not just the one
/// <see cref="GatewayRoutingTests"/> uses): each tenant's own domain must reach its own silo,
/// never the other tenant's.
/// </summary>
public sealed class GatewayMultiTenantSiloRoutingTests : IAsyncLifetime
{
    private WebApplication _sharedBackend = null!;
    private WebApplication _acmeSiloBackend = null!;
    private WebApplication _globexSiloBackend = null!;
    private TestServer _gateway = null!;

    public async Task InitializeAsync()
    {
        _sharedBackend = await TestBackend.StartAsync("shared");
        _acmeSiloBackend = await TestBackend.StartAsync("acme-silo");
        _globexSiloBackend = await TestBackend.StartAsync("globex-silo");

        var destinations = new GatewayRouteCatalog.ServiceDestinations(
            TestBackend.Address(_sharedBackend),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["acme.example.com"] = TestBackend.Address(_acmeSiloBackend),
                ["globex.example.com"] = TestBackend.Address(_globexSiloBackend),
            });
        var (routes, clusters) = GatewayRouteCatalog.Build(destinations, destinations, destinations);

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services.AddReverseProxy().LoadFromMemory(routes, clusters));
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapReverseProxy());
                });
            })
            .StartAsync();

        _gateway = host.GetTestServer();
    }

    [Theory]
    [InlineData("acme.example.com", "acme-silo")]
    [InlineData("globex.example.com", "globex-silo")]
    public async Task EachTenantDomain_RoutesToItsOwnSiloOnly(string tenantDomain, string expectedBackend)
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, tenantDomain);

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be(expectedBackend, $"'{tenantDomain}' must reach its own silo, never another tenant's");
    }

    public async Task DisposeAsync()
    {
        _gateway.Dispose();
        await _sharedBackend.DisposeAsync();
        await _acmeSiloBackend.DisposeAsync();
        await _globexSiloBackend.DisposeAsync();
    }

    private sealed record BackendIdentity(string Backend);
}
