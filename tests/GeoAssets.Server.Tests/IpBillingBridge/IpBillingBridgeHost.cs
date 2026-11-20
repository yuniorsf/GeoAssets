using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using GeoAssets.IpBilling.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// The real <c>GeoAssets.IpBilling.Server</c> <c>Program</c> (a <see cref="WebApplicationFactory{TEntryPoint}"/> keyed on one of the
/// host's types — <c>Program</c> is ambiguous in this project) with the kernel replaced by <see cref="StubKernel"/> for every
/// outgoing <see cref="HttpClient"/>, the CIAM bearer validation by <see cref="BridgeTokenAuthenticationHandler"/> and the
/// service-caller token source by <see cref="SwitchableServiceAuthTokenProvider"/>. The registration retries every few
/// milliseconds. An exception no middleware handled answers 500, as Kestrel does (TestServer would rethrow it to the caller).
/// </summary>
internal sealed class IpBillingBridgeHost : IAsyncDisposable
{
    public const string KernelBaseUrl = "http://kernel.test";

    private readonly WebApplicationFactory<MunicipioProvisioningService> _factory;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _unhandled;

    private IpBillingBridgeHost(WebApplicationFactory<MunicipioProvisioningService> factory, StubKernel kernel, KernelLink link, SwitchableServiceAuthTokenProvider tokens, System.Collections.Concurrent.ConcurrentQueue<Exception> unhandled)
    {
        _factory = factory;
        _unhandled = unhandled;
        Kernel = kernel;
        Link = link;
        ServiceTokens = tokens;
    }

    public StubKernel Kernel { get; }

    /// <summary>The exceptions no middleware handled (each one a 500), in order.</summary>
    public IReadOnlyList<Exception> Unhandled => [.. _unhandled];

    public KernelLink Link { get; }

    public SwitchableServiceAuthTokenProvider ServiceTokens { get; }

    public static async Task<IpBillingBridgeHost> StartAsync(bool kernelRegistrationFails = false)
    {
        var kernel = new StubKernel { RegistrationFails = kernelRegistrationFails };
        await kernel.StartAsync();
        var link = new KernelLink(kernel.CreateHandler());
        var tokens = new SwitchableServiceAuthTokenProvider();
        var unhandled = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        var factory = new WebApplicationFactory<MunicipioProvisioningService>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:IpBilling", "Host=localhost;Port=1;Database=never_connected;Username=x;Password=x;Timeout=1");
            host.UseSetting("GeoCoreServer:BaseUrl", KernelBaseUrl);
            host.UseSetting("Observability:Otlp:Endpoint", string.Empty);
            host.UseSetting("VerticalRegistration:InitialDelay", "00:00:00.020");
            host.UseSetting("VerticalRegistration:MaxDelay", "00:00:00.100");
            host.UseSetting("VerticalRegistration:RefreshInterval", "00:00:00.200");

            host.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = link));
                services.AddSingleton<IServiceAuthTokenProvider>(tokens);
                services
                    .AddAuthentication(o =>
                    {
                        o.DefaultScheme = BridgeTokenAuthenticationHandler.SchemeName;
                        o.DefaultAuthenticateScheme = BridgeTokenAuthenticationHandler.SchemeName;
                        o.DefaultChallengeScheme = BridgeTokenAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<BridgeTokenAuthenticationOptions, BridgeTokenAuthenticationHandler>(BridgeTokenAuthenticationHandler.SchemeName, _ => { });
                services.AddTransient<IStartupFilter>(_ => new AnswerUnhandledExceptionsWith500(unhandled));
            });
        });

        _ = factory.Services; // starts the host and its registration loop
        return new IpBillingBridgeHost(factory, kernel, link, tokens, unhandled);
    }

    public IServiceProvider Services => _factory.Services;

    /// <summary>A client whose requests carry <paramref name="token"/> as their bearer token; none for <c>null</c>.</summary>
    public HttpClient ClientFor(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Task.Delay(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (deadline.IsCompleted) throw new TimeoutException("The host did not reach the expected state in time.");
            await Task.Delay(10);
        }
    }

    public static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Task.Delay(TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            if (deadline.IsCompleted) throw new TimeoutException("The host did not reach the expected state in time.");
            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Kernel.DisposeAsync();
        Link.Dispose();
    }

    private sealed class AnswerUnhandledExceptionsWith500(System.Collections.Concurrent.ConcurrentQueue<Exception> unhandled) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                try
                {
                    await continuation();
                }
                catch (Exception exception) when (!context.Response.HasStarted)
                {
                    unhandled.Enqueue(exception);
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            });
            next(app);
        };
    }
}
