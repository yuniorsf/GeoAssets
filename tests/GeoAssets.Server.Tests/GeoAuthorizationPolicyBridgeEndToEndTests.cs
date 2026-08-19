using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
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
/// No-op authentication scheme — just gives AuthorizationMiddlewareResultHandler a default
/// scheme to Challenge/Forbid against. Actual authentication in these tests happens via the
/// middleware that sets <c>HttpContext.User</c> directly, standing in for XD01-12's real
/// bearer-token validation.
/// </summary>
internal sealed class NoOpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}

/// <summary>
/// Proves <c>AddGeoAuthorizationPolicyBridge()</c> actually wires the custom policy
/// provider/handler into the real ASP.NET Core pipeline via <c>.RequireAuthorization("Name")</c>
/// — the unit tests in <see cref="GeoAuthorizationPolicyProviderTests"/>/
/// <see cref="GeoAuthorizationHandlerTests"/> can't catch a DI registration mistake
/// (e.g. forgetting to register the provider, or the wrong lifetime).
/// </summary>
public class GeoAuthorizationPolicyBridgeEndToEndTests
{
    private sealed class FakeAuthorizationService(bool grant) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => Task.FromResult(grant);
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync(bool grant)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    // AuthorizationMiddlewareResultHandler calls IAuthenticationService.Forbid/
                    // ChallengeAsync on denial — needs a default scheme registered even though
                    // this test sets HttpContext.User directly instead of running a real scheme.
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService(grant));
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                });
                webHost.Configure(app =>
                {
                    // Stand-in for real bearer-token authentication (XD01-12) — sets
                    // HttpContext.User directly so RequireAuthenticatedUser() sees an
                    // authenticated principal without needing a real JWT/IdP round-trip.
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet("/protected", () => "ok").RequireAuthorization("CanEditFeatures"));
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task RequireAuthorization_PolicyGranted_Returns200()
    {
        using var server = await BuildServerAsync(grant: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireAuthorization_PolicyDenied_Returns403()
    {
        using var server = await BuildServerAsync(grant: false);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireAuthorization_Unauthenticated_Returns401()
    {
        using var server = await BuildServerAsync(grant: true);
        using var client = server.CreateClient();

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
