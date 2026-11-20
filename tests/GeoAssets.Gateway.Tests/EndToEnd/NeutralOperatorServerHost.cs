extern alias NoServer;

using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NoServerEntry = NoServer::GeoAssets.NeutralOperator.Server.Migrations.InitialNeutralOperatorIsolation;

namespace GeoAssets.Gateway.Tests.EndToEnd;

/// <summary>
/// The real <c>GeoAssets.NeutralOperator.Server</c> <c>Program</c> over a migrated Testcontainers database of its own, with the
/// kernel it calls (identity, global target ledger, registration) replaced by <see cref="Kernel"/> for every outgoing
/// <see cref="HttpClient"/>, the CIAM bearer validation replaced by <see cref="BearerTokenAuthenticationHandler"/> and the
/// service-caller token source by <see cref="FakeServiceAuthTokenProvider"/> — the same stand-ins as the host's own test suite
/// (<c>GeoAssets.NeutralOperator.Server.Tests</c>), whose host class is internal to that assembly. <see cref="CreateHandler"/> is
/// what a gateway forwards to when this host is its destination.
/// </summary>
internal sealed class NeutralOperatorServerHost : IAsyncDisposable
{
    public const string KernelBaseUrl = "http://kernel-of-the-vertical.test";

    private readonly WebApplicationFactory<NoServerEntry> _factory;

    private NeutralOperatorServerHost(WebApplicationFactory<NoServerEntry> factory, FakeKernel kernel, string connectionString)
    {
        _factory = factory;
        Kernel = kernel;
        ConnectionString = connectionString;
    }

    public FakeKernel Kernel { get; }

    public string ConnectionString { get; }

    public IServiceProvider Services => _factory.Services;

    public static async Task<NeutralOperatorServerHost> StartAsync()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        var kernel = new FakeKernel(TestIdentities.ByToken);

        var factory = new WebApplicationFactory<NoServerEntry>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:NeutralOperator", connectionString);
            host.UseSetting("GeoCoreServer:BaseUrl", KernelBaseUrl);
            host.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            host.UseSetting("VerticalRegistration:InitialDelay", "00:00:00.020");
            host.UseSetting("VerticalRegistration:MaxDelay", "00:00:00.100");
            host.UseSetting("Workflow:Occupancy:PoleAssetTypeIds:0", "pole");

            host.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = kernel));
                services.AddSingleton<IServiceAuthTokenProvider, FakeServiceAuthTokenProvider>();
                services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
                    () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
                    new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = [BearerTokenAuthenticationHandler.OrganizationClaim] }));
                services
                    .AddAuthentication(o =>
                    {
                        o.DefaultScheme = BearerTokenAuthenticationHandler.SchemeName;
                        o.DefaultAuthenticateScheme = BearerTokenAuthenticationHandler.SchemeName;
                        o.DefaultChallengeScheme = BearerTokenAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(BearerTokenAuthenticationHandler.SchemeName, _ => { });
            });
        });

        _ = factory.Services; // starts the host (and the registration loop)
        return new NeutralOperatorServerHost(factory, kernel, connectionString);
    }

    /// <summary>The in-process handler of the host: a gateway's forwarder sends to it when the host is the destination.</summary>
    public HttpMessageHandler CreateHandler() => _factory.Server.CreateHandler();

    /// <summary>A client whose requests carry <paramref name="token"/> as their bearer token; none for <c>null</c>.</summary>
    public HttpClient ClientFor(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
