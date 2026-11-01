using System.Net;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class ObservabilityApplicationExtensionsTests
{
    /// <summary>
    /// Stands in for real bearer-token authentication — same pattern as
    /// <c>GeoAssets.FieldService.Server.Tests.HostWiringTests.NoOpAuthenticationHandler</c>. Never
    /// authenticates anyone; its only purpose here is to give <c>AddAuthentication</c> a
    /// resolvable default scheme so <c>RequireAuthenticatedUser()</c> has something to challenge
    /// against for an anonymous request.
    /// </summary>
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                // Mirrors GeoAssets.Server/Program.cs: AddHealthChecks() must be registered
                // for UseHealthChecks (called inside UseGeoAssetsObservability) to resolve
                // HealthCheckService — omitting it throws at request time, not at startup.
                webHost.ConfigureServices(services => services.AddHealthChecks());
                webHost.Configure(app => app.UseGeoAssetsObservability());
            })
            .StartAsync();

        return host.GetTestServer();
    }

    /// <summary>
    /// Documents a real contract gap this test uncovered: <see cref="ObservabilityApplicationExtensions.UseGeoAssetsObservability"/>
    /// calls <c>UseHealthChecks("/healthz", ...)</c>, which throws <see cref="InvalidOperationException"/>
    /// at host-startup time (not lazily on first request) if <c>services.AddHealthChecks()</c> was
    /// never called. <c>apps/GeoAssets.Server/Program.cs</c> called <c>UseGeoAssetsObservability()</c>
    /// without ever calling <c>AddHealthChecks()</c> — meaning the real server could not start at all.
    /// Fixed alongside this test (added the missing <c>AddHealthChecks()</c> call to
    /// <c>GeoAssets.Server/Program.cs</c>); this test guards the contract going forward.
    /// </summary>
    [Fact]
    public Task Healthz_AddHealthChecksNeverCalled_ThrowsAtStartup()
    {
        var act = () => new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app => app.UseGeoAssetsObservability());
            })
            .StartAsync();

        return act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AddHealthChecks*");
    }

    [Fact]
    public async Task Healthz_NoRegisteredChecks_ReturnsHealthyWith200()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        body.Should().Be("{\"status\":\"healthy\"}");
    }

    [Fact]
    public async Task Healthz_UnhealthyRegisteredCheck_Returns503WithUnhealthyStatus()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services
                    .AddHealthChecks()
                    .AddCheck("always-down", () => HealthCheckResult.Unhealthy()));
                webHost.Configure(app => app.UseGeoAssetsObservability());
            })
            .StartAsync();
        using var server = host.GetTestServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().Be("{\"status\":\"unhealthy\"}");
    }

    [Fact]
    public async Task OtherPaths_AreNotIntercepted_ByHealthCheckMiddleware()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services.AddHealthChecks());
                webHost.Configure(app =>
                {
                    app.UseGeoAssetsObservability();
                    app.Run(ctx => ctx.Response.WriteAsync("downstream"));
                });
            })
            .StartAsync();
        using var server = host.GetTestServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/features", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("downstream");
    }

    /// <summary>
    /// Regression test for a real bug (XD01-215): every host wired via
    /// <c>GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication</c> (GeoAssets.Server,
    /// GeoAssets.FieldService.Server) sets a <c>FallbackPolicy</c> requiring an authenticated
    /// user on every endpoint without an endpoint-specific policy. Discovered live: deploying
    /// GeoAssets.FieldService.Server's real container image to a kind cluster under XD01-215's
    /// Helm chart, Kubernetes' liveness/readiness probes (which carry no bearer token) got 401
    /// from <c>/healthz</c> and the pod never became Ready. Uses minimal-hosting
    /// <see cref="WebApplication"/> (not the bare <see cref="IApplicationBuilder"/>
    /// <c>HostBuilder</c> pipeline the other tests in this file use) because only
    /// <see cref="WebApplication"/> is also an <c>IEndpointRouteBuilder</c> — the exact condition
    /// <see cref="ObservabilityApplicationExtensions.UseGeoAssetsObservability"/> branches on.
    /// </summary>
    [Fact]
    public async Task Healthz_RemainsReachable_EvenWhenFallbackPolicyRequiresAuthentication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseGeoAssetsObservability();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a Kubernetes liveness/readiness probe carries no bearer token and must not be rejected by the FallbackPolicy");

        await app.StopAsync();
    }
}
