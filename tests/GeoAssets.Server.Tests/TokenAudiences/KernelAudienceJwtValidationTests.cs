using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// XD01-390: the kernel's real bearer validation (<c>AddGeoAssetsAuthentication</c> → the Entra CIAM provider, so
/// Microsoft.Identity.Web's audience rules and the <c>AzureAdCiam:AdditionalAudiences</c> widening) over locally signed tokens.
/// Only the audience set widens; issuer, signature and lifetime are still enforced. The host is the kernel's authentication
/// registration plus one authenticated route — the provider class itself is internal to <c>GeoAssets.Server</c>.
/// </summary>
public sealed class KernelAudienceJwtValidationTests
{
    private const string Route = "/whoami";

    private static Task<IHost> StartAsync(params string?[] additionalAudiences) =>
        new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddGeoAssetsAuthentication(new KernelJwtAuthentication(additionalAudiences).Configuration());
                    LocalJwt.TrustLocalSigning(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGet(Route, (HttpContext context) => context.User.FindFirst("aud")?.Value ?? "no-audience"));
                });
            })
            .StartAsync();

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(IHost host, string? token)
    {
        using var client = host.GetTestClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(new Uri(Route, UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ── no AdditionalAudiences: Microsoft.Identity.Web's own audience validation ──────────────────────────────────

    [Fact]
    public async Task WithoutAdditionalAudiences_ATokenForTheKernelsOwnAudience_IsAccepted()
    {
        using var host = await StartAsync();

        var (status, body) = await GetAsync(host, LocalJwt.Mint(LocalJwt.KernelAudience));

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be(LocalJwt.KernelAudience);
    }

    [Fact]
    public async Task WithoutAdditionalAudiences_ATokenForAVerticalsAudience_IsRefused()
    {
        using var host = await StartAsync();

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience))).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithOnlyBlankAdditionalAudiences_TheKernelDoesNotWiden()
    {
        using var host = await StartAsync("", "  ", null);

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, LocalJwt.Mint(LocalJwt.KernelAudience))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithoutAnyToken_TheKernelAnswers401()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        (await GetAsync(host, token: null)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── AdditionalAudiences = [vertical-api] ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithTheVerticalsAudienceListed_ATokenForIt_IsAccepted()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        var (status, body) = await GetAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience));

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be(LocalJwt.VerticalAudience);
    }

    [Fact]
    public async Task WithTheVerticalsAudienceListed_TheKernelsOwnAudienceIsStillAccepted()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.KernelAudience))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithTheVerticalsAudienceListed_TheKernelsApiSchemeAudienceIsAccepted()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        (await GetAsync(host, LocalJwt.Mint("api://" + LocalJwt.KernelAudience))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithTheVerticalsAudienceListed_AnAudienceNotListed_IsRefused()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        (await GetAsync(host, LocalJwt.Mint("some-other-api"))).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithSeveralAudiencesListed_EachOfThemIsAccepted()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience, " second-vertical ");

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience))).Status.Should().Be(HttpStatusCode.OK);
        (await GetAsync(host, LocalJwt.Mint("second-vertical"))).Status.Should().Be(HttpStatusCode.OK);
    }

    // ── only the audience set widens ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenWidened_AnExpiredToken_IsStillRefused()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience, expired: true))).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenWidened_ATokenOfAnotherIssuer_IsStillRefused()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, issuer: "https://other-tenant.local.test/v2.0");

        (await GetAsync(host, token)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenWidened_ATokenSignedWithAnotherKey_IsStillRefused()
    {
        using var host = await StartAsync(LocalJwt.VerticalAudience);

        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, signingKey: LocalJwt.NewKey());

        (await GetAsync(host, token)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenNotWidened_AnExpiredTokenForTheKernelsAudience_IsRefused()
    {
        using var host = await StartAsync();

        (await GetAsync(host, LocalJwt.Mint(LocalJwt.KernelAudience, expired: true))).Status.Should().Be(HttpStatusCode.Unauthorized);
    }
}
