using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Provider.WFS;
using GeoAssets.Web.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-477: through the DI the Web host registers, a WFS entry on the GeoAssets gateway carries the signed-in user's
/// token and one on any other WFS server none — with or without a route scope configured — and an entry's tenant domain
/// travels as its requests' <c>X-Tenant-Domain</c> header, only when configured and only on that entry.
/// </summary>
public sealed class WfsProviderAuthorizationTests
{
    private const string Gateway = "http://gw";
    private const string Kernel = "api://kernel/access_as_user";
    private const string NeutralOperator = "api://no/access_as_user";

    private const string Capabilities =
        """<wfs:WFS_Capabilities xmlns:wfs="http://www.opengis.net/wfs/2.0" version="2.0.0"><wfs:FeatureTypeList /></wfs:WFS_Capabilities>""";

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://gw/", "http://gw/");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
        }
    }

    private sealed class FakeTokenProvider : IAccessTokenProvider
    {
        public ValueTask<AccessTokenResult> RequestAccessToken() => throw new NotSupportedException();

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
        {
            var token = new AccessToken { Value = $"token-for:{string.Join(' ', options.Scopes ?? [])}", Expires = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            return ValueTask.FromResult(new AccessTokenResult(AccessTokenResultStatus.Success, token, null, null));
        }
    }

    private sealed record Sent(Uri Uri, string? Authorization, string? TenantDomain);

    private sealed class Recorder : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tenant = request.Headers.TryGetValues(WfsProviderFactory.TenantDomainHeader, out var values) ? string.Join(',', values) : null;
            Requests.Add(new Sent(request.RequestUri!, request.Headers.Authorization?.ToString(), tenant));
            var capabilities = request.RequestUri!.Query.Contains("REQUEST=GetCapabilities", StringComparison.Ordinal);
            var content = capabilities
                ? new StringContent(Capabilities, Encoding.UTF8, "application/xml")
                : new StringContent("""{"type":"FeatureCollection","features":[]}""", Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static IConfiguration Configuration(bool routeScoped)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["GeoAssetsServer:ApiScope"] = Kernel };
        if (routeScoped)
        {
            values["GeoAssetsServer:RouteScopes:0:PathPrefix"] = "/api/v/neutral-operator/";
            values["GeoAssetsServer:RouteScopes:0:Scope"] = NeutralOperator;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Build(Recorder recorder, bool routeScoped = false)
    {
        var configuration = Configuration(routeScoped);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenProvider>(new FakeTokenProvider());
        services.AddSingleton<NavigationManager>(new FakeNavigation());
        services.AddTransient<AuthorizationMessageHandler>();
        services.AddGeoAssetsServerClient(configuration, Gateway + "/").ConfigurePrimaryHttpMessageHandler(() => recorder);
        services.AddWebProviderPlugins(configuration);
        return services.BuildServiceProvider();
    }

    private static Task<IAssetProvider> ConnectAsync(ServiceProvider provider, string url, string? tenantDomain = null)
    {
        var config = new ProviderConfig();
        config.Set("url", url);
        if (tenantDomain is not null)
            config.Set("tenantDomain", tenantDomain);

        return provider.GetServices<IProviderPlugin>().Single(p => string.Equals(p.Id, "wfs", StringComparison.Ordinal)).CreateAsync(config, provider);
    }

    [Fact]
    public void TheWfsFactory_RequestsTheWebHostsTokenCarryingClient()
    {
        WfsProviderFactory.HttpClientName.Should().Be(GeoAssetsServerClientExtensions.ClientName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWfsEntryOnTheGateway_CarriesTheKernelToken_OnEveryRequest(bool routeScoped)
    {
        var recorder = new Recorder();
        using var provider = Build(recorder, routeScoped);

        await ConnectAsync(provider, Gateway);

        recorder.Requests.Should().HaveCount(2);
        recorder.Requests.Should().OnlyContain(r => r.Authorization == $"Bearer token-for:{Kernel}");
        recorder.Requests.Select(r => r.Uri.AbsolutePath).Should().Equal("/wfs/", "/wfs/");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWfsEntryOnAnotherServer_CarriesNoToken(bool routeScoped)
    {
        var recorder = new Recorder();
        using var provider = Build(recorder, routeScoped);

        await ConnectAsync(provider, "https://ogc.example.org/geoserver/wfs");

        recorder.Requests.Should().HaveCount(2);
        recorder.Requests.Should().OnlyContain(r => r.Authorization == null && r.Uri.Host == "ogc.example.org");
    }

    [Fact]
    public async Task ATenantDomain_TravelsAsXTenantDomain_OnEveryRequestOfThatEntryOnly()
    {
        var recorder = new Recorder();
        using var provider = Build(recorder);

        await ConnectAsync(provider, Gateway, tenantDomain: " coelba.local ");
        await ConnectAsync(provider, Gateway);

        recorder.Requests.Select(r => r.TenantDomain).Should().Equal("coelba.local", "coelba.local", null, null);
        recorder.Requests.Should().OnlyContain(r => r.Authorization == $"Bearer token-for:{Kernel}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTenantDomain_SendsNoHeader(string tenantDomain)
    {
        var recorder = new Recorder();
        using var provider = Build(recorder);

        await ConnectAsync(provider, Gateway, tenantDomain);

        recorder.Requests.Should().HaveCount(2).And.OnlyContain(r => r.TenantDomain == null);
    }

    [Theory]
    [InlineData("coelba local")]
    [InlineData("http://coelba.local")]
    [InlineData("coelba.local/x")]
    [InlineData("-coelba.local")]
    [InlineData("coelba.local\r\nX-Injected: 1")]
    public async Task AnInvalidTenantDomain_IsRefusedAtCreate_BeforeAnyRequest(string tenantDomain)
    {
        var recorder = new Recorder();
        using var provider = Build(recorder);

        InvalidOperationException? refused = null;
        try
        {
            await ConnectAsync(provider, Gateway, tenantDomain);
        }
        catch (InvalidOperationException ex)
        {
            refused = ex;
        }

        refused.Should().NotBeNull();
        refused!.Message.Should().Contain("not a host name");
        recorder.Requests.Should().BeEmpty();
    }

    [Fact]
    public void TheUrlField_SaysTheGatewayAndThatWfsIsAppended_AndTheTenantDomainFieldIsOptional()
    {
        var recorder = new Recorder();
        using var provider = Build(recorder);
        var fields = provider.GetServices<IProviderPlugin>().Single(p => string.Equals(p.Id, "wfs", StringComparison.Ordinal)).ConfigFields;

        var url = fields.Single(f => string.Equals(f.Key, "url", StringComparison.Ordinal));
        url.Label.Should().Contain("gateway").And.Contain("/wfs is appended").And.NotContain("REST");
        url.Placeholder.Should().NotContain("/api/geoassets");
        fields.Single(f => string.Equals(f.Key, "tenantDomain", StringComparison.Ordinal)).Required.Should().BeFalse();
    }
}
