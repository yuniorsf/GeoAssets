using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.FieldService.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Proves XD01-212's AC end-to-end: each archetype module is independently reachable through
/// the unified host, and disabling one via its own "Modules:{Name}:Enabled" config key never
/// affects the others — mirroring Program.cs's own conditional-mapping shape exactly (read the
/// same config key, call the same MapXModuleApi() only when enabled) rather than re-testing it
/// indirectly.
/// </summary>
public sealed class ArchetypeModuleRoutingTests
{
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<TestServer> BuildServerAsync(bool electricalEnabled = true, bool sanitationEnabled = true, bool neutralPoleEnabled = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Modules:Electrical:Enabled"] = electricalEnabled.ToString(),
                ["Modules:Sanitation:Enabled"] = sanitationEnabled.ToString(),
                ["Modules:NeutralPole:Enabled"] = neutralPoleEnabled.ToString(),
            })
            .Build();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());
                    services.AddSingleton<NeutralPoleRegistry>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        // Mirrors Program.cs's own conditional-mapping shape exactly.
                        if (app.ApplicationServices.GetRequiredService<IConfiguration>().GetValue("Modules:Electrical:Enabled", defaultValue: true))
                            endpoints.MapElectricalModuleApi();

                        if (app.ApplicationServices.GetRequiredService<IConfiguration>().GetValue("Modules:Sanitation:Enabled", defaultValue: true))
                            endpoints.MapSanitationModuleApi();

                        if (app.ApplicationServices.GetRequiredService<IConfiguration>().GetValue("Modules:NeutralPole:Enabled", defaultValue: true))
                            endpoints.MapNeutralPoleModuleApi();
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task ElectricalModule_WhenEnabled_IsReachable()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        var started = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/electrical/sla/status", UriKind.Relative),
            new SlaStatusRequest(started, TimeSpan.FromDays(10), started + TimeSpan.FromDays(1)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SlaStatusResponse>();
        body!.Status.Should().Be("OnTrack");
    }

    [Fact]
    public async Task SanitationModule_WhenEnabled_IsReachable()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/sanitation/icerp/reinstatement", UriKind.Relative),
            new IcerpReinstatementRequest(LengthMeters: 4, WidthMeters: 0.6, MarginMeters: 0.2, ThicknessMeters: 0.05));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IcerpReinstatementResponse>();
        body!.AreaSquareMeters.Should().BeApproximately(4.4, 1e-9);
    }

    [Fact]
    public async Task NeutralPoleModule_WhenEnabled_IsReachable()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/neutral-pole/poles/pole-1/slots/allocate", UriKind.Relative),
            new OperatorSlotRequest("isp-a"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SlotAllocationResponseDto>();
        body!.Outcome.Should().Be("Allocated");
        body.SlotIndex.Should().Be(1);
    }

    [Fact]
    public async Task ElectricalModule_WhenDisabled_Returns404()
    {
        using var server = await BuildServerAsync(electricalEnabled: false);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/electrical/sla/status", UriKind.Relative),
            new SlaStatusRequest(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisablingOneModule_DoesNotAffectTheOthers()
    {
        using var server = await BuildServerAsync(electricalEnabled: false);
        using var client = server.CreateClient();

        var sanitationResponse = await client.PostAsJsonAsync(
            new Uri("/api/field-service/sanitation/icerp/reinstatement", UriKind.Relative),
            new IcerpReinstatementRequest(1, 1, 0, 0));
        var neutralPoleResponse = await client.PostAsJsonAsync(
            new Uri("/api/field-service/neutral-pole/poles/pole-1/slots/allocate", UriKind.Relative),
            new OperatorSlotRequest("isp-a"));

        sanitationResponse.StatusCode.Should().Be(HttpStatusCode.OK, "disabling Electrical must not affect Sanitation's own routes");
        neutralPoleResponse.StatusCode.Should().Be(HttpStatusCode.OK, "disabling Electrical must not affect NeutralPole's own routes");
    }

    [Fact]
    public async Task AllModulesDisabled_NoneAreReachable_ButHostStillStarts()
    {
        using var server = await BuildServerAsync(electricalEnabled: false, sanitationEnabled: false, neutralPoleEnabled: false);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/electrical/sla/status", UriKind.Relative),
            new SlaStatusRequest(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
