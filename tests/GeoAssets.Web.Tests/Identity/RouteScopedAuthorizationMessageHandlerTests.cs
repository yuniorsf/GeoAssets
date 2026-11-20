using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>XD01-397: the handler asks for exactly the scope of the route a request falls under, and only for requests to the gateway.</summary>
public sealed class RouteScopedAuthorizationMessageHandlerTests
{
    private const string Kernel = "api://kernel/access_as_user";
    private const string NeutralOperator = "api://no/access_as_user";

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("https://gw/", "https://gw/");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
        }
    }

    private sealed class FakeTokenProvider(bool grant = true) : IAccessTokenProvider
    {
        public List<string[]> Requested { get; } = [];

        public ValueTask<AccessTokenResult> RequestAccessToken()
        {
            throw new NotSupportedException();
        }

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
        {
            Requested.Add(options.Scopes?.ToArray() ?? []);
            var token = grant ? new AccessToken { Value = $"token-for:{options.Scopes!.Single()}", Expires = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) } : null;
            return ValueTask.FromResult(new AccessTokenResult(grant ? AccessTokenResultStatus.Success : AccessTokenResultStatus.RequiresRedirect, token!, grant ? null : "authentication/login", null));
        }
    }

    private static (HttpMessageInvoker Invoker, FakeTokenProvider Provider, FakeHttpMessageHandler Inner) Build(bool grant = true, string? defaultScope = Kernel)
    {
        var provider = new FakeTokenProvider(grant);
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var routes = new ApiScopeRoutes(new Uri("https://gw/"), defaultScope, [new ApiScopeRoute("/api/v/neutral-operator/", NeutralOperator)]);
        var handler = new RouteScopedAuthorizationMessageHandler(provider, new FakeNavigation(), routes) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), provider, inner);
    }

    private static HttpRequestMessage Get(string uri) => new(HttpMethod.Get, uri);

    [Fact]
    public async Task ARouteRequest_CarriesTheRoutesToken()
    {
        var (invoker, provider, inner) = Build();

        using var request = Get("https://gw/api/v/neutral-operator/workflow/service-orders");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", $"token-for:{NeutralOperator}"));
        provider.Requested.Should().ContainSingle().Which.Should().Equal(NeutralOperator);
    }

    [Fact]
    public async Task AKernelRequest_CarriesTheDefaultScopesToken()
    {
        var (invoker, provider, inner) = Build();

        using var request = Get("https://gw/api/workflow/service-orders");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Single().Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", $"token-for:{Kernel}"));
        provider.Requested.Should().ContainSingle().Which.Should().Equal(Kernel);
    }

    [Fact]
    public async Task EachCall_RequestsExactlyOneScope()
    {
        var (invoker, provider, _) = Build();

        using var first = Get("https://gw/api/v/neutral-operator/x");
        using var second = Get("https://gw/api/x");
        using var r1 = await invoker.SendAsync(first, CancellationToken.None);
        using var r2 = await invoker.SendAsync(second, CancellationToken.None);

        provider.Requested.Should().HaveCount(2).And.AllSatisfy(scopes => scopes.Should().HaveCount(1));
    }

    [Fact]
    public async Task ARequestOutsideTheGateway_GetsNoToken_AndTheProviderIsNotAsked()
    {
        var (invoker, provider, inner) = Build();

        using var request = Get("https://elsewhere.example/api/v/neutral-operator/x");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Single().Headers.Authorization.Should().BeNull();
        provider.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task ARequestWithoutAUri_IsSentUnchanged()
    {
        var (invoker, provider, inner) = Build();

        using var request = new HttpRequestMessage();
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Should().ContainSingle();
        provider.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenNoScopeApplies_NoTokenIsAttached()
    {
        var (invoker, provider, inner) = Build(defaultScope: null);

        using var request = Get("https://gw/api/workflow/service-orders");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Single().Headers.Authorization.Should().BeNull();
        provider.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheProviderHasNoToken_AccessTokenNotAvailableExceptionIsThrown()
    {
        var (invoker, _, inner) = Build(grant: false);

        using var request = Get("https://gw/api/v/neutral-operator/x");
        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<AccessTokenNotAvailableException>();
        inner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnExistingAuthorizationHeader_IsReplaced()
    {
        var (invoker, _, inner) = Build();

        using var request = Get("https://gw/api/workflow/x");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stale");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Requests.Single().Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", $"token-for:{Kernel}"));
    }

    [Fact]
    public void NullConstructorArguments_AreRefused()
    {
        var routes = new ApiScopeRoutes(new Uri("https://gw/"), Kernel);

        var noProvider = () => new RouteScopedAuthorizationMessageHandler(null!, new FakeNavigation(), routes);
        var noNavigation = () => new RouteScopedAuthorizationMessageHandler(new FakeTokenProvider(), null!, routes);
        var noRoutes = () => new RouteScopedAuthorizationMessageHandler(new FakeTokenProvider(), new FakeNavigation(), null!);

        noProvider.Should().Throw<ArgumentNullException>();
        noNavigation.Should().Throw<ArgumentNullException>();
        noRoutes.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ANullRequest_IsRefused()
    {
        var routes = new ApiScopeRoutes(new Uri("https://gw/"), Kernel);
        using var handler = new RouteScopedAuthorizationMessageHandler(new FakeTokenProvider(), new FakeNavigation(), routes)
        {
            InnerHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
        };
        using var invoker = new HttpMessageInvoker(handler);

        var act = async () => await invoker.SendAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
