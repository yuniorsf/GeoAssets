using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class ObservabilityApplicationExtensionsTests
{
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
    public async Task Healthz_AddHealthChecksNeverCalled_ThrowsAtStartup()
    {
        var act = () => new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app => app.UseGeoAssetsObservability());
            })
            .StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AddHealthChecks*");
    }

    [Fact]
    public async Task Healthz_NoRegisteredChecks_ReturnsHealthyWith200()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/healthz");
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

        var response = await client.GetAsync("/healthz");
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

        var response = await client.GetAsync("/features");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("downstream");
    }
}
