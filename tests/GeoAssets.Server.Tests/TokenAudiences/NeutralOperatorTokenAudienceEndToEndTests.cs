using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Server.Tests.IpBillingBridge;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;
using NeutralOperatorEntryPoint = GeoAssets.NeutralOperator.Server.HttpTenantContext;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// XD01-397 (AC: end to end). A client that requests the NeutralOperator API's own scope for <c>/api/v/neutral-operator/</c>
/// (<c>GeoAssetsServer:RouteScopes</c>) sends a token for that audience. The real <c>GeoAssets.NeutralOperator.Server</c> host —
/// its own CIAM bearer validation (<c>AzureAdCiam:ClientId</c> = <see cref="LocalJwt.VerticalAudience"/>), over a migrated
/// Testcontainers database of its own — serves the request, and the caller's token it forwards to the kernel's
/// <c>/api/identity/me</c> is accepted by the kernel's real bearer validation (<see cref="StubKernel"/> in its JWT mode) when the
/// kernel lists that audience in <c>AzureAdCiam:AdditionalAudiences</c>; without it the host answers 502
/// <c>kernel.token-rejected</c>. The host's HTTP clients are routed into the kernel's TestServer.
/// </summary>
public sealed class NeutralOperatorTokenAudienceEndToEndTests
{
    private const string KernelBaseUrl = "http://kernel.test";
    private static readonly Uri OrderList = new("/api/workflow/service-orders", UriKind.Relative);
    private static readonly string[] ReadPermission = ["serviceorders:read"];

    [Fact]
    public async Task WhenTheKernelListsTheNeutralOperatorAudience_TheHostServesTheRequest_AndTheKernelAcceptedTheForwardedToken()
    {
        await using var run = await NeutralOperatorRun.StartAsync(LocalJwt.VerticalAudience);
        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: ReadPermission);

        using var client = run.ClientFor(token);
        using var response = await client.GetAsync(OrderList);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", body);
        run.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().NotBeEmpty()
            .And.OnlyContain(r => r.Token == token, "the caller's own token is what the kernel validated");
    }

    [Fact]
    public async Task WhenTheKernelDoesNotListTheNeutralOperatorAudience_TheHostAnswers502TokenRejected()
    {
        await using var run = await NeutralOperatorRun.StartAsync();
        var token = LocalJwt.Mint(LocalJwt.VerticalAudience, permissions: ReadPermission);

        using var client = run.ClientFor(token);
        using var response = await client.GetAsync(OrderList);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "the body was: {0}", body);
        body.Should().Contain("\"code\":\"kernel.token-rejected\"").And.Contain("AzureAdCiam:AdditionalAudiences");
        run.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ATokenForTheKernelsAudience_IsRefusedByTheNeutralOperatorHost_SoItNeverReachesTheKernel()
    {
        await using var run = await NeutralOperatorRun.StartAsync(LocalJwt.VerticalAudience);

        using var client = run.ClientFor(LocalJwt.Mint(LocalJwt.KernelAudience, permissions: ReadPermission));
        using var response = await client.GetAsync(OrderList);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        run.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().BeEmpty();
    }

    /// <summary>The kernel (real JWT validation) and the real NeutralOperator host whose HTTP clients reach it.</summary>
    private sealed class NeutralOperatorRun : IAsyncDisposable
    {
        private readonly WebApplicationFactory<NeutralOperatorEntryPoint> _factory;
        private readonly HttpMessageHandler _kernelHandler;

        private NeutralOperatorRun(StubKernel kernel, HttpMessageHandler kernelHandler, WebApplicationFactory<NeutralOperatorEntryPoint> factory)
        {
            Kernel = kernel;
            _kernelHandler = kernelHandler;
            _factory = factory;
        }

        public StubKernel Kernel { get; }

        public static async Task<NeutralOperatorRun> StartAsync(params string[] kernelAdditionalAudiences)
        {
            var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
            var kernel = new StubKernel();
            await kernel.StartAsync(new KernelJwtAuthentication(kernelAdditionalAudiences));
            var kernelHandler = kernel.CreateHandler();
            var serviceTokens = new SwitchableServiceAuthTokenProvider
            {
                Token = LocalJwt.Mint(LocalJwt.KernelAudience, roles: [BridgeCallers.ServiceCallerRole]),
            };

            var factory = new WebApplicationFactory<NeutralOperatorEntryPoint>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Testing");
                host.UseSetting("ConnectionStrings:NeutralOperator", connectionString);
                host.UseSetting("GeoCoreServer:BaseUrl", KernelBaseUrl);
                host.UseSetting("Observability:Otlp:Endpoint", string.Empty);
                host.UseSetting("AzureAdCiam:Instance", "https://contoso.ciamlogin.com/");
                host.UseSetting("AzureAdCiam:TenantId", "00000000-0000-4000-8000-000000000390");
                host.UseSetting("AzureAdCiam:ClientId", LocalJwt.VerticalAudience);

                host.ConfigureTestServices(services =>
                {
                    services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = kernelHandler));
                    services.AddSingleton<IServiceAuthTokenProvider>(serviceTokens);
                    LocalJwt.TrustLocalSigning(services);
                });
            });

            _ = factory.Services; // starts the host
            return new NeutralOperatorRun(kernel, kernelHandler, factory);
        }

        public HttpClient ClientFor(string token)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await Kernel.DisposeAsync();
            _kernelHandler.Dispose();
        }
    }
}
