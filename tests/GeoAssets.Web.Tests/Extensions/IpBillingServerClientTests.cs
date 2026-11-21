using System.Net;
using FluentAssertions;
using GeoAssets.Web.Extensions;
using GeoAssets.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Authentication.WebAssembly.Msal.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-343: the IP-billing client requests the IP-billing API's own scope, or the kernel's while that is blank; sign-in
/// consents to the IP-billing scope instead of adding a second resource to <c>DefaultAccessTokenScopes</c>; and the
/// shipped settings request no placeholder scope.
/// </summary>
public sealed class IpBillingServerClientTests
{
    private const string IpBilling = "http://ipbilling";
    private const string KernelScope = "api://kernel/access_as_user";
    private const string IpBillingScope = "api://ipbilling/access_as_user";

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://web/", "http://web/");

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

    private sealed class Recorder : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.Ordinal)).Build();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output.");
    }

    private static async Task<string?> AuthorizationSentAsync(IConfiguration configuration)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton<IAccessTokenProvider>(new FakeTokenProvider());
        services.AddSingleton<NavigationManager>(new FakeNavigation());
        services.AddTransient<AuthorizationMessageHandler>();
        services.AddGeoAssetsIpBillingServerClient(configuration, IpBilling + "/").ConfigurePrimaryHttpMessageHandler(() => recorder);
        using var provider = services.BuildServiceProvider();

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IpBillingServerClientExtensions.ClientName);
        using var response = await http.GetAsync(new Uri(IpBilling + "/api/ip-billing/municipios"));

        return recorder.Authorizations.Should().ContainSingle().Subject;
    }

    private static MsalProviderOptions SignInOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager>(new FakeNavigation());
        new EntraCiamWasmAuthenticationProvider().AddAuthentication(services, configuration);
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<RemoteAuthenticationOptions<MsalProviderOptions>>>().Value.ProviderOptions;
    }

    [Fact]
    public async Task WithItsOwnScope_TheClientCarriesTheIpBillingToken()
    {
        var authorization = await AuthorizationSentAsync(Configuration(
            ("GeoAssetsServer:ApiScope", KernelScope),
            ("GeoAssetsIpBillingServer:ApiScope", IpBillingScope)));

        authorization.Should().Be($"Bearer token-for:{IpBillingScope}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithABlankScope_TheClientCarriesTheKernelToken(string? ipBillingScope)
    {
        var values = new List<(string, string)> { ("GeoAssetsServer:ApiScope", KernelScope) };
        if (ipBillingScope is not null)
            values.Add(("GeoAssetsIpBillingServer:ApiScope", ipBillingScope));

        var authorization = await AuthorizationSentAsync(Configuration([.. values]));

        authorization.Should().Be($"Bearer token-for:{KernelScope}");
    }

    [Fact]
    public void WithNoScopeAtAll_TheResolvedScopeIsEmpty()
    {
        IpBillingServerClientExtensions.ResolveApiScope(Configuration()).Should().BeEmpty();
        IpBillingServerClientExtensions.OwnApiScope(Configuration()).Should().BeNull();
    }

    [Fact]
    public void SignIn_ConsentsToTheIpBillingScope_AndRequestsOnlyTheKernelsByDefault()
    {
        var options = SignInOptions(Configuration(
            ("GeoAssetsServer:BaseUrl", "http://gw/"),
            ("GeoAssetsServer:ApiScope", KernelScope),
            ("GeoAssetsIpBillingServer:ApiScope", IpBillingScope)));

        options.DefaultAccessTokenScopes.Should().Contain(KernelScope).And.NotContain(IpBillingScope);
        options.AdditionalScopesToConsent.Should().Contain(IpBillingScope);
    }

    [Theory]
    [InlineData("")]
    [InlineData(KernelScope)]
    public void SignIn_AsksForNoExtraConsent_WhenTheIpBillingScopeIsBlankOrTheKernels(string ipBillingScope)
    {
        var options = SignInOptions(Configuration(
            ("GeoAssetsServer:BaseUrl", "http://gw/"),
            ("GeoAssetsServer:ApiScope", KernelScope),
            ("GeoAssetsIpBillingServer:ApiScope", ipBillingScope)));

        options.DefaultAccessTokenScopes.Should().Contain(KernelScope);
        options.AdditionalScopesToConsent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Local.json.example")]
    public void TheShippedWebSettings_RequestNoPlaceholderIpBillingScope(string file)
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Web", "wwwroot", file));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        configuration.GetSection("GeoAssetsIpBillingServer").Exists().Should().BeTrue();
        IpBillingServerClientExtensions.OwnApiScope(configuration).Should().BeNull();
    }

    [Fact]
    public void TheWebProgram_UsesTheClientExtension()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Web", "Program.cs"));

        program.Should().Contain("AddGeoAssetsIpBillingServerClient(")
            .And.NotContain("AddHttpClient(\"GeoAssetsIpBillingServer\")");
    }
}
