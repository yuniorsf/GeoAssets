using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// XD01-216's cross-tenant penetration test pass against the Gateway: adversarial inputs a
/// pentest would specifically try against <see cref="GatewayRouteCatalog"/>'s route matching and
/// <see cref="OrganizationIdPartitionKeyResolver"/>'s claim extraction, on top of the "happy
/// path" coverage <see cref="GatewayRoutingTests"/>/<see cref="GatewayRateLimitingTests"/>
/// already have.
/// </summary>
public sealed class GatewayAdversarialInputTests : IAsyncLifetime
{
    private const string OrganizationClaimType = "org_id";

    private WebApplication _sharedBackend = null!;
    private WebApplication _siloBackend = null!;
    private TestServer _gateway = null!;

    public async Task InitializeAsync()
    {
        _sharedBackend = await TestBackend.StartAsync("shared");
        _siloBackend = await TestBackend.StartAsync("silo");

        var destinations = new GatewayRouteCatalog.ServiceDestinations(TestBackend.Address(_sharedBackend), TestBackend.Address(_siloBackend));
        var (routes, clusters) = GatewayRouteCatalog.Build(destinations, destinations, destinations);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["RateLimiting:TokenLimit"] = "1000",
                ["RateLimiting:TokensPerPeriod"] = "1000",
                ["RateLimiting:ReplenishmentPeriodSeconds"] = "3600",
                ["RateLimiting:OrganizationIdClaimType"] = OrganizationClaimType,
            })
            .Build();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddReverseProxy().LoadFromMemory(routes, clusters);
                    services.AddTenantRateLimiting(configuration);
                });
                webHost.Configure(app =>
                {
                    app.UseRateLimiter();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapReverseProxy());
                });
            })
            .StartAsync();

        _gateway = host.GetTestServer();
    }

    /// <summary>
    /// Documents a real finding from this audit (tracked separately, not fixed here — see
    /// XD01-216's audit report under docs/security/): <see cref="GatewayRouteCatalog"/>'s silo
    /// route matches on <see cref="HeaderMatchMode.Exists"/> alone, never the header's value, and
    /// there is exactly one silo destination per Gateway deployment (not one per tenant domain).
    /// Today that is a traffic-steering ambiguity, not a cross-tenant data leak: whichever silo
    /// the Gateway proxies to still enforces its own real bearer-token authentication
    /// (RequireAuthenticatedUser FallbackPolicy) before any data is returned, same as proven live
    /// in XD01-215. It becomes a real routing-correctness problem the moment a second Tier-2
    /// silo exists (XD01-217/218), since this Gateway cannot yet pick between them by header
    /// value — tracked as a follow-up rather than redesigned under this audit ticket.
    /// </summary>
    [Theory]
    [InlineData("acme.example.com")]
    [InlineData("ACME.EXAMPLE.COM")]
    [InlineData("some-other-tenant-entirely.example.com")]
    [InlineData("x")]
    public async Task AnyNonEmptyTenantDomainHeaderValue_RoutesToSiloCluster_RegardlessOfValue(string headerValue)
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, headerValue);

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be("silo",
            "the current route match is HeaderMatchMode.Exists — any non-empty value reaches the one configured silo destination, not just a known tenant's own domain");
    }

    [Fact]
    public async Task TenantDomainHeader_WithSqlInjectionLikeValue_IsHandledSafelyAndStillRoutesToSilo()
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, "'; DROP TABLE geo_entity; --");

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Gateway never interpolates header values into SQL — YARP's header match is a literal existence check — so a SQL-injection-shaped value must be inert, not cause a crash");
    }

    [Theory]
    [InlineData("Bearer not-a-jwt-at-all")]
    [InlineData("Bearer ")]
    [InlineData("NotEvenBearer abc.def.ghi")]
    public async Task MalformedOrGarbageAuthorizationHeader_FallsIntoUnresolvedBucket_WithoutCrashing(string authorizationHeader)
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "OrganizationIdPartitionKeyResolver.CanReadToken must reject a malformed/garbage token into the shared 'unresolved' bucket, never throw and take the Gateway down");
    }

    public async Task DisposeAsync()
    {
        _gateway.Dispose();
        await _sharedBackend.DisposeAsync();
        await _siloBackend.DisposeAsync();
    }

    private sealed record BackendIdentity(string Backend);
}
