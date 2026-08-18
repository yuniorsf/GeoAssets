using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAssetsAuthenticationExtensionsTests
{
    private static IConfiguration BuildCiamConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAdCiam:Instance"] = "https://contoso.ciamlogin.com/",
                ["AzureAdCiam:TenantId"] = "11111111-2222-3333-4444-555555555555",
                ["AzureAdCiam:ClientId"] = "66666666-7777-8888-9999-aaaaaaaaaaaa",
            })
            .Build();

    [Fact]
    public void AddGeoAssetsAuthentication_BindsJwtBearerAuthorityFromCiamConfiguration()
    {
        var configuration = BuildCiamConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);

        services.AddGeoAssetsAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtOptions.Authority.Should().Contain("contoso.ciamlogin.com");
        jwtOptions.Authority.Should().Contain("11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void AddGeoAssetsAuthentication_RegistersFallbackPolicyRequiringAuthenticatedUser()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddGeoAssetsAuthentication(BuildCiamConfiguration());

        using var provider = services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        authOptions.FallbackPolicy.Should().NotBeNull();
        authOptions.FallbackPolicy!.Requirements
            .Should().ContainItemsAssignableTo<DenyAnonymousAuthorizationRequirement>();
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var configuration = BuildCiamConfiguration();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddRouting();
                    services.AddGeoAssetsAuthentication(configuration);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseGeoAssetsAuthentication();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", () => "ok");
                        endpoints.MapGet("/public", () => "ok").AllowAnonymous();
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task ProtectedEndpoint_NoAuthorizationHeader_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowAnonymousEndpoint_NoAuthorizationHeader_Returns200()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/public");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
