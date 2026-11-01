using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
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
/// Proves XD01-214's AC: one organization exceeding its token bucket is throttled (429) without
/// affecting another organization's requests, or their latency. A focused, deterministic
/// concurrency test against a tiny, test-only bucket (3 tokens, an hour-long replenishment
/// period so it never refills mid-test) — not a sustained-throughput load-testing harness like
/// k6/NBomber, which would be a separate tooling investment outside this suite.
///
/// Forwards through a real YARP proxy (<see cref="GatewayRateLimiting.AddTenantRateLimiting"/>,
/// the same extension method <c>Program.cs</c> calls) to a real Kestrel backend, so the
/// rate-limiting middleware genuinely runs in the pipeline rather than being exercised directly
/// against its options object.
/// </summary>
public sealed class GatewayRateLimitingTests : IAsyncLifetime
{
    private const string OrganizationClaimType = "org_id";

    private WebApplication _backend = null!;
    private TestServer _gateway = null!;

    public async Task InitializeAsync()
    {
        _backend = await TestBackend.StartAsync("backend");
        var destinations = new GatewayRouteCatalog.ServiceDestinations(TestBackend.Address(_backend), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var (routes, clusters) = GatewayRouteCatalog.Build(destinations, destinations, destinations);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["RateLimiting:TokenLimit"] = "3",
                ["RateLimiting:TokensPerPeriod"] = "3",
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

    private static string BuildToken(string organizationId)
    {
        var token = new JwtSecurityToken(claims: [new Claim(OrganizationClaimType, organizationId)]);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HttpRequestMessage BuildRequest(string? organizationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/geoassets/widgets", UriKind.Relative));
        if (organizationId is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken(organizationId));
        return request;
    }

    [Fact]
    public async Task OrganizationExceedingItsBucket_Is429_WithoutAffectingAnotherOrganization()
    {
        using var client = _gateway.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.SendAsync(BuildRequest("org-a"));
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1} is within org-a's 3-token bucket");
        }

        using var fourthResponse = await client.SendAsync(BuildRequest("org-a"));
        fourthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "org-a's 4th request exceeds its bucket");

        var stopwatch = Stopwatch.StartNew();
        using var otherOrgResponse = await client.SendAsync(BuildRequest("org-b"));
        stopwatch.Stop();

        otherOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK, "exhausting org-a's bucket must never consume org-b's own tokens");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "org-b's request must not be queued or delayed by org-a's throttling");
    }

    [Fact]
    public async Task RequestsWithNoBearerToken_ShareOneUnresolvedBucket()
    {
        using var client = _gateway.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.SendAsync(BuildRequest(organizationId: null));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var fourthResponse = await client.SendAsync(BuildRequest(organizationId: null));

        fourthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    public async Task DisposeAsync()
    {
        _gateway.Dispose();
        await _backend.DisposeAsync();
    }
}
