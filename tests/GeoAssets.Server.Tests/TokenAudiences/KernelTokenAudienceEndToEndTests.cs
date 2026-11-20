using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GeoAssets.Server.Tests.IpBillingBridge;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// XD01-390 (AC: two audiences). The real <c>GeoAssets.IpBilling.Server</c> host validates the caller's token for its own
/// audience (<see cref="LocalJwt.VerticalAudience"/>) and forwards it to a kernel whose bearer validation is the real one
/// (<c>AddGeoAssetsAuthentication</c>, the kernel's own identity endpoints, locally signed tokens): the token reaches the kernel's
/// <c>/api/identity/me</c> and is accepted when the kernel lists that audience in <c>AzureAdCiam:AdditionalAudiences</c>, and is
/// refused otherwise — the vertical then answers a distinct 502 <c>kernel.token-rejected</c>, not <c>kernel.unavailable</c>.
/// The kernel is the existing in-process <see cref="StubKernel"/> in its JWT mode, reached through the vertical's HTTP clients
/// exactly as the Município bridge tests do (the vertical's <c>HttpClient</c>s are routed into the kernel's TestServer).
/// </summary>
public sealed class KernelTokenAudienceEndToEndTests
{
    private const string Sync = "/api/ip-billing/municipio-grants/sync";
    private const string ManagePermission = "ip-billing:reference-manage";
    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000390");
    private static readonly Guid Prefeitura = new("a0a0a0a0-0000-4000-8000-000000000390");

    private static async Task<(StubKernel Kernel, IpBillingBridgeHost Host)> StartAsync(params string[] kernelAdditionalAudiences)
    {
        var kernel = new StubKernel();
        await kernel.StartAsync(new KernelJwtAuthentication(kernelAdditionalAudiences));
        var host = await IpBillingBridgeHost.StartAsync(startedKernel: kernel, configureServices: services =>
        {
            services
                .AddAuthentication(o =>
                {
                    o.DefaultScheme = VerticalJwtAuthenticationHandler.SchemeName;
                    o.DefaultAuthenticateScheme = VerticalJwtAuthenticationHandler.SchemeName;
                    o.DefaultChallengeScheme = VerticalJwtAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, VerticalJwtAuthenticationHandler>(VerticalJwtAuthenticationHandler.SchemeName, _ => { });
        });

        // The grant write is the vertical's own service principal: a kernel-audience token carrying the ServiceCaller app role.
        host.ServiceTokens.Token = LocalJwt.Mint(LocalJwt.KernelAudience, roles: [BridgeCallers.ServiceCallerRole]);
        return (kernel, host);
    }

    private static async Task<HttpResponseMessage> SyncAsync(IpBillingBridgeHost host, string token)
    {
        using var client = host.ClientFor(token);
        return await client.PostAsJsonAsync(
            new Uri(Sync, UriKind.Relative),
            new
            {
                ibgeCode = "3550308",
                distribuidoraOrganizationId = Distribuidora,
                prefeituraOrganizationId = Prefeitura,
                previousPrefeituraOrganizationId = (Guid?)null,
                grantedBy = "reference-manager@test",
            });
    }

    [Fact]
    public async Task WhenTheKernelListsTheVerticalsAudience_TheForwardedToken_ReachesIdentityMe_AndTheVerticalEndpointSucceeds()
    {
        var (kernel, host) = await StartAsync(LocalJwt.VerticalAudience);
        await using var _ = host;
        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: [ManagePermission]);

        using var response = await SyncAsync(host, token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        kernel.To(HttpMethod.Get, "/api/identity/me").Should().ContainSingle()
            .Which.Token.Should().Be(token, "the caller's own token is what the kernel validated");
        (await kernel.ActiveGrantsAsync(Prefeitura, Distribuidora)).Should().ContainSingle();
        host.Unhandled.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheKernelDoesNotListTheVerticalsAudience_TheVerticalAnswers502TokenRejected_NotKernelUnavailable()
    {
        var (kernel, host) = await StartAsync();
        await using var _ = host;
        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: [ManagePermission]);

        using var response = await SyncAsync(host, token);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "the body was: {0}", body);
        body.Should().Contain("\"code\":\"kernel.token-rejected\"").And.NotContain("kernel.unavailable");
        body.Should().Contain("AzureAdCiam:AdditionalAudiences");
        host.Unhandled.Should().BeEmpty();
        kernel.To(HttpMethod.Get, "/api/identity/me").Should().ContainSingle();
        kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheKernelRefusesTheToken_ItsErrorDescription_IsIncludedInTheProblemDetail()
    {
        var (_, host) = await StartAsync();
        await using var _ = host;

        using var response = await SyncAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: [ManagePermission]));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("The kernel said:");
        body.Should().Contain("The audience", "the kernel's own invalid_token description is relayed as it sent it");
    }

    [Fact]
    public async Task WhenTheKernelListsAnotherAudienceOnly_TheVerticalsToken_IsStillRefused()
    {
        var (_, host) = await StartAsync("some-other-vertical-api");
        await using var _ = host;

        using var response = await SyncAsync(host, LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: [ManagePermission]));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("kernel.token-rejected");
    }

    [Fact]
    public async Task ATokenForTheKernelsOwnAudience_IsNotAcceptedByTheVertical_SoItNeverReachesTheKernel()
    {
        var (kernel, host) = await StartAsync();
        await using var _ = host;

        using var response = await SyncAsync(host, LocalJwt.Mint(LocalJwt.KernelAudience, permissions: [ManagePermission]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        kernel.To(HttpMethod.Get, "/api/identity/me").Should().BeEmpty();
    }
}
