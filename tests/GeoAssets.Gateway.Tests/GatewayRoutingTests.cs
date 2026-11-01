using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// Proves XD01-213's route-order precedence end-to-end: a real YARP proxy (via
/// <see cref="GatewayRouteCatalog"/>, the exact route/cluster table <c>Program.cs</c> builds)
/// forwarding real HTTP requests to two real Kestrel backends on OS-assigned loopback ports —
/// not stubbed-out routing internals, so this exercises YARP's actual precedence logic, not an
/// assumption about how it behaves.
/// </summary>
public sealed class GatewayRoutingTests : IAsyncLifetime
{
    private WebApplication _sharedBackend = null!;
    private WebApplication _siloBackend = null!;
    private TestServer _gateway = null!;

    public async Task InitializeAsync()
    {
        _sharedBackend = await StartBackendAsync("shared");
        _siloBackend = await StartBackendAsync("silo");

        var destinations = new GatewayRouteCatalog.ServiceDestinations(BackendAddress(_sharedBackend), BackendAddress(_siloBackend));
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

    private static async Task<WebApplication> StartBackendAsync(string identity)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/{**catchAll}", () => Results.Ok(new { backend = identity }));
        await app.StartAsync();
        return app;
    }

    private static string BackendAddress(WebApplication app) => app.Urls.Single();

    [Fact]
    public async Task RequestWithoutTenantDomainHeader_FallsThroughToSharedCluster()
    {
        using var client = _gateway.CreateClient();

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be("shared");
    }

    [Fact]
    public async Task RequestWithTenantDomainHeader_RoutesToSiloClusterDespiteMatchingTheSamePathAsTheSharedRoute()
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, "acme.example.com");

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be("silo", "a silo tenant's request must win over the shared route even though both match /api/geoassets/widgets");
    }

    [Fact]
    public async Task RoutePrecedenceAppliesConsistently_AcrossEveryProxiedServicePrefix()
    {
        using var withHeader = _gateway.CreateClient();
        withHeader.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, "acme.example.com");
        using var withoutHeader = _gateway.CreateClient();

        foreach (var prefix in new[] { "/api/geoassets/x", "/api/ip-billing/x", "/api/field-service/x" })
        {
            var silo = await (await withHeader.GetAsync(new Uri(prefix, UriKind.Relative))).Content.ReadFromJsonAsync<BackendIdentity>();
            var shared = await (await withoutHeader.GetAsync(new Uri(prefix, UriKind.Relative))).Content.ReadFromJsonAsync<BackendIdentity>();

            silo!.Backend.Should().Be("silo", $"{prefix} must honor the silo route when the tenant-domain header is present");
            shared!.Backend.Should().Be("shared", $"{prefix} must fall through to the shared route otherwise");
        }
    }

    public async Task DisposeAsync()
    {
        _gateway.Dispose();
        await _sharedBackend.DisposeAsync();
        await _siloBackend.DisposeAsync();
    }

    private sealed record BackendIdentity(string Backend);
}
