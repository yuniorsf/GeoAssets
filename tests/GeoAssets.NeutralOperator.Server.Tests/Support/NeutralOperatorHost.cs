using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>
/// The real <c>GeoAssets.NeutralOperator.Server</c> <c>Program</c> (a <see cref="WebApplicationFactory{TEntryPoint}"/> keyed on
/// <see cref="HttpTenantContext"/>, any type of the host assembly) over a migrated Testcontainers database of its own, with the
/// kernel replaced by <see cref="Kernel"/> for every outgoing <see cref="HttpClient"/>, the CIAM bearer validation replaced by
/// <see cref="BearerTokenAuthenticationHandler"/>, and the service-caller token source replaced by
/// <see cref="FakeServiceAuthTokenProvider"/>. The bootstrap registration retries every few milliseconds, so a test never waits
/// on the production backoff.
/// </summary>
internal sealed class NeutralOperatorHost : IAsyncDisposable
{
    public const string KernelBaseUrl = "http://kernel.test";

    private readonly WebApplicationFactory<HttpTenantContext> _factory;

    private NeutralOperatorHost(WebApplicationFactory<HttpTenantContext> factory, FakeKernel kernel, string connectionString)
    {
        _factory = factory;
        Kernel = kernel;
        ConnectionString = connectionString;
    }

    public FakeKernel Kernel { get; }

    public string ConnectionString { get; }

    public IServiceProvider Services => _factory.Services;

    public static async Task<NeutralOperatorHost> StartAsync(
        Action<FakeKernel>? configureKernel = null,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        string? connectionString = null)
    {
        connectionString ??= await NeutralOperatorDatabase.CreateMigratedAsync();
        var kernel = new FakeKernel(TestIdentities.ByToken);
        configureKernel?.Invoke(kernel);

        var factory = new WebApplicationFactory<HttpTenantContext>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:NeutralOperator", connectionString);
            host.UseSetting("GeoCoreServer:BaseUrl", KernelBaseUrl);
            host.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            host.UseSetting("VerticalRegistration:InitialDelay", "00:00:00.020");
            host.UseSetting("VerticalRegistration:MaxDelay", "00:00:00.100");
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>(StringComparer.Ordinal))
                host.UseSetting(key, value);

            host.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = kernel));
                services.AddSingleton<IServiceAuthTokenProvider, FakeServiceAuthTokenProvider>();

                // The default Entra claim mapping carries no organization claim, which leaves the host's tenant context empty
                // for every caller): these tests run as a deployment whose tokens do carry one.
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
                configureServices?.Invoke(services);
            });
        });

        _ = factory.Services; // starts the host (and the registration loop)
        return new NeutralOperatorHost(factory, kernel, connectionString);
    }

    /// <summary>A client whose requests carry <paramref name="token"/> as their bearer token; none for <c>null</c>.</summary>
    public HttpClient ClientFor(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
