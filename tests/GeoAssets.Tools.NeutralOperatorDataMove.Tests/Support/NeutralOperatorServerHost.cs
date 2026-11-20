using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using GeoAssets.NeutralOperator.Server.Migrations;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>
/// The real <c>GeoAssets.NeutralOperator.Server</c> <c>Program</c> over the target database the tool copied into, with the
/// kernel replaced by <see cref="Kernel"/> (identity and registration only) and the CIAM bearer validation by
/// <see cref="BearerTokenAuthenticationHandler"/> — the way that host's own suite boots it. The factory is keyed on a public type
/// of the host's assembly (its first migration), the host's own entry-point marker being internal to its suite.
/// </summary>
internal sealed class NeutralOperatorServerHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<InitialNeutralOperatorIsolation> _factory;

    private NeutralOperatorServerHost(WebApplicationFactory<InitialNeutralOperatorIsolation> factory, FakeKernel kernel)
    {
        _factory = factory;
        Kernel = kernel;
    }

    public FakeKernel Kernel { get; }

    /// <param name="connectionString">The database the host serves.</param>
    /// <param name="sagaApplyTimeout">
    /// How long a pending approval saga may wait before the host's sweeper flags it (stamping its row). The default is a century, so a host
    /// started only to read the moved orders never writes to them; a test of the sweeper's interplay with the move passes a short one.
    /// </param>
    /// <param name="sweepInterval">How often that sweeper looks.</param>
    public static NeutralOperatorServerHost Start(string connectionString, string sagaApplyTimeout = "36500.00:00:00", string sweepInterval = "00:01:00")
    {
        var kernel = new FakeKernel(TestIdentities.ByToken);
        var factory = new WebApplicationFactory<InitialNeutralOperatorIsolation>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:NeutralOperator", connectionString);
            host.UseSetting("GeoCoreServer:BaseUrl", "http://kernel.test");
            host.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            host.UseSetting("VerticalRegistration:InitialDelay", "00:00:00.020");
            host.UseSetting("VerticalRegistration:MaxDelay", "00:00:00.100");
            host.UseSetting("Saga:ApplyTimeout", sagaApplyTimeout);
            host.UseSetting("Saga:SweepInterval", sweepInterval);

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

        _ = factory.Services;
        return new NeutralOperatorServerHost(factory, kernel);
    }

    /// <summary>A client whose requests carry <paramref name="token"/> as their bearer token.</summary>
    public HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
