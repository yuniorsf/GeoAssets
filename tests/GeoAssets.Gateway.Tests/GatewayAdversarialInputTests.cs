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

        var destinations = new GatewayRouteCatalog.ServiceDestinations(
            TestBackend.Address(_sharedBackend),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["acme.example.com"] = TestBackend.Address(_siloBackend) });
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
    /// XD01-289 fixed the finding this test used to document: <see cref="GatewayRouteCatalog"/>'s
    /// silo route now matches the <see cref="GatewayRouteCatalog.TenantDomainHeader"/>'s exact value (case-insensitively)
    /// against the set of provisioned tenant domains, not merely the header's presence — only a
    /// known tenant's own domain reaches a silo at all, any other value (including a value that
    /// happens to look like another tenant's domain, or garbage) falls through to the shared
    /// cluster.
    /// </summary>
    [Theory]
    [InlineData("some-other-tenant-entirely.example.com")]
    [InlineData("x")]
    [InlineData("")]
    public async Task UnprovisionedTenantDomainHeaderValue_FallsThroughToSharedCluster_NeverAnArbitrarySilo(string headerValue)
    {
        using var client = _gateway.CreateClient();
        if (!string.IsNullOrEmpty(headerValue))
            client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, headerValue);

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be("shared",
            "only a header value matching a provisioned tenant domain may reach a silo — everything else, including a well-formed but unknown domain, falls through to shared");
    }

    [Theory]
    [InlineData("acme.example.com")]
    [InlineData("ACME.EXAMPLE.COM")]
    public async Task ProvisionedTenantDomainHeaderValue_RoutesToItsOwnSilo_RegardlessOfCase(string headerValue)
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, headerValue);

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        body!.Backend.Should().Be("silo", "a provisioned tenant's own domain, in any case, must reach its silo");
    }

    [Fact]
    public async Task TenantDomainHeader_WithSqlInjectionLikeValue_IsHandledSafelyAndFallsThroughToShared()
    {
        using var client = _gateway.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayRouteCatalog.TenantDomainHeader, "'; DROP TABLE geo_entity; --");

        var response = await client.GetAsync(new Uri("/api/geoassets/widgets", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<BackendIdentity>();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Gateway never interpolates header values into SQL — YARP's header match is a literal value comparison — so a SQL-injection-shaped value must be inert, not cause a crash");
        body!.Backend.Should().Be("shared", "an injection-shaped value matches no provisioned tenant domain, so it falls through to shared like any other unknown value");
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
