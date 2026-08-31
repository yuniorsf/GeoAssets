using System.Net;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves the real production GET /api/workflow/service-orders/* endpoints are gated by
/// "serviceorders:read" (XD01-127) — added alongside the ServiceOrders REST client auth fix so a
/// caller who can reach these routes at all is actually authorized to read Service Orders, not
/// merely authenticated. Write endpoints are deliberately untouched — already covered by
/// <see cref="ServiceOrderRulesEndpointTests"/>, whose finer-grained, per-order business rules a
/// blanket permission code would not improve (see ServiceOrdersRestApiExtensions' class doc
/// comment for why).
/// </summary>
public class ServiceOrdersReadAuthorizationTests
{
    private sealed class FakeAuthorizationService(HashSet<string> grantedPermissions) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
            => Task.FromResult(grantedPermissions.Contains(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync(params string[] grantedPermissions)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOrderTypeRegistry();
                    services.AddWorkflowInMemory();
                    services.AddServiceOrderRules();
                    services.AddScoped<ServerWorkflowPrincipalFactory>();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService([.. grantedPermissions]));
                    // Never actually called by these read-only tests — registered only because
                    // MapServiceOrdersApi's write endpoints also need ServerWorkflowPrincipalFactory
                    // to build their delegate metadata when the endpoint set is constructed, same
                    // reasoning as ServiceOrderRulesEndpointTests.BuildServerAsync.
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                });
                webHost.Configure(app =>
                {
                    // Stand-in for real bearer-token authentication (XD01-12), same pattern as
                    // EndpointAuthorizationTests.
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapServiceOrdersApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static HttpClient AuthenticatedClient(TestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    [Fact]
    public async Task GetServiceOrders_Unauthenticated_Returns401()
    {
        using var server = await BuildServerAsync("serviceorders:read");
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/workflow/service-orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetServiceOrders_MissingReadPermission_Returns403()
    {
        // Non-leakage: being authenticated is not enough — must hold serviceorders:read.
        using var server = await BuildServerAsync(); // authenticated, no permissions granted
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/workflow/service-orders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetServiceOrders_HasReadPermission_Returns200()
    {
        using var server = await BuildServerAsync("serviceorders:read");
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/workflow/service-orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetServiceOrderById_MissingReadPermission_Returns403()
    {
        using var server = await BuildServerAsync();
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/workflow/service-orders/some-id");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
