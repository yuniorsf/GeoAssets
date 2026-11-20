using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// <see cref="ForwardedBearerTokenHandler"/> (XD01-382): the caller's own bearer token is what the kernel's identity routes are asked
/// with — a Bearer token only, nothing else the caller sent, and nothing outside a request.
/// </summary>
public sealed class ForwardedBearerTokenHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FixedHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static async Task<HttpRequestMessage> SendAsync(HttpContext? context, Action<HttpRequestMessage>? configure = null)
    {
        var inner = new CapturingHandler();
        using var handler = new ForwardedBearerTokenHandler(new FixedHttpContextAccessor(context)) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://kernel.test/api/identity/me"));
        configure?.Invoke(request);

        using var response = await client.SendAsync(request);

        return inner.Seen!;
    }

    private static DefaultHttpContext Context(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        return context;
    }

    [Fact]
    public async Task ABearerTokenOfTheCurrentRequest_IsForwardedAsItIs()
    {
        var seen = await SendAsync(Context("Bearer caller-token"));

        seen.Headers.Authorization!.ToString().Should().Be("Bearer caller-token");
    }

    [Fact]
    public async Task TheBearerSchemeIsRecognisedInAnyCase()
    {
        var seen = await SendAsync(Context("bearer caller-token"));

        seen.Headers.Authorization!.Parameter.Should().Be("caller-token");
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("caller-token")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NothingThatIsNotABearerToken_IsForwarded(string authorization)
    {
        var seen = await SendAsync(Context(authorization));

        seen.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ARequestWithoutAuthorization_ForwardsNone()
    {
        var seen = await SendAsync(Context(null));

        seen.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task OutsideARequest_NothingIsForwarded()
    {
        var seen = await SendAsync(context: null);

        seen.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task OtherHeadersOfTheCurrentRequest_AreNeverForwarded()
    {
        var context = Context("Bearer caller-token");
        context.Request.Headers["X-Organization-Id"] = TestIdentities.OtherOrganization.ToString();
        context.Request.Headers.Cookie = "session=abc";

        var seen = await SendAsync(context);

        seen.Headers.Select(h => h.Key).Should().BeEquivalentTo(["Authorization"]);
    }
}
