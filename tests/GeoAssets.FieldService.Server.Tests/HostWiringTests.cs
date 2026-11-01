using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Proves this host's actual DI/middleware wiring (XD01-206's AC) end-to-end against a real
/// ASP.NET Core <see cref="TestServer"/> pipeline — not just that the individual pieces
/// (<see cref="HttpTenantContextTests"/>) work in isolation.
///
/// Stands in for real bearer-token authentication the same way
/// <c>GeoAuthorizationPolicyBridgeEndToEndTests</c> in GeoAssets.Server.Tests does: a
/// no-op authentication scheme plus a middleware that sets <c>HttpContext.User</c> directly
/// from a request header, so a simulated authenticated principal's claims flow through the
/// exact same <c>ICurrentUserAccessor</c>/<c>HttpTenantContext</c> chain Program.cs wires,
/// without needing a real CIAM token round-trip.
/// </summary>
public sealed class HostWiringTests
{
    private const string OrganizationClaimType = "org_id";

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<TestServer> BuildServerAsync(ClaimMapping? claimMapping = null)
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
                            claimMapping));
                    services.AddScoped<ITenantContext, HttpTenantContext>();
                });
                webHost.Configure(app =>
                {
                    // Stand-in for real bearer-token authentication — sets HttpContext.User
                    // directly from a request header so RequireAuthenticatedUser() (and
                    // HttpTenantContext downstream) see a real claims principal without a
                    // CIAM round-trip.
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var orgId))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(OrganizationClaimType, orgId.ToString())], "TestScheme"));
                        else if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", (ITenantContext tenantContext)
                            => tenantContext.CurrentOrganizationId.ToString());
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task ProtectedEndpoint_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/protected", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "every endpoint requires an authenticated caller by default, matching GeoAssets.Server's fallback policy");
    }

    [Fact]
    public async Task ProtectedEndpoint_AuthenticatedWithOrganizationClaim_ResolvesTenantContextFromClaim()
    {
        var organizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var server = await BuildServerAsync(new ClaimMapping { OrganizationIdClaimTypes = [OrganizationClaimType] });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", organizationId.ToString());

        var response = await client.GetAsync(new Uri("/protected", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(organizationId.ToString());
    }

    [Fact]
    public async Task ProtectedEndpoint_AuthenticatedWithoutOrganizationClaimConfigured_ResolvesEmptyGuid()
    {
        // Matches GeoAssets.Server's actual current behavior (XD01-200): Program.cs constructs
        // ClaimsPrincipalCurrentUserAccessor with the default ClaimMapping.EntraDefault, which
        // has no OrganizationIdClaimTypes configured, so every authenticated caller resolves to
        // Guid.Empty until an operator configures a real org claim for their CIAM tenant.
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(new Uri("/protected", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(Guid.Empty.ToString());
    }
}
