using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// XD01-384: what the gateway does to a request and to the answer, through its own <c>Program</c> (route table, CORS, rate
/// limiter, observability pipeline) in front of two real loopback backends — the kernel's and the NeutralOperator host's.
/// </summary>
public sealed class GatewayProxyBehaviourTests : IAsyncLifetime
{
    private const string VerticalRoute = "/api/v/neutral-operator/workflow/service-orders/o1";

    private RecordingBackend _kernel = null!;
    private RecordingBackend _neutralOperator = null!;

    public async Task InitializeAsync()
    {
        _kernel = await RecordingBackend.StartAsync("kernel", context =>
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            return Task.CompletedTask;
        });
        _neutralOperator = await RecordingBackend.StartAsync("neutral-operator", context =>
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Access-Control-Expose-Headers"] = "X-Backend-Only";
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync()
    {
        await _kernel.DisposeAsync();
        await _neutralOperator.DisposeAsync();
    }

    private GatewayFactory Gateway(params (string Key, string Value)[] more) =>
        GatewayFactory.For(_kernel, _neutralOperator, more.ToDictionary(m => m.Key, m => (string?)m.Value, StringComparer.Ordinal));

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    // ── Path rewrite ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AVerticalRoute_IsRewrittenToTheApiPrefix_QueryKept_AndReachesOnlyTheVerticalsHost()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative($"{VerticalRoute}?x=1&y=a%20b"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = _neutralOperator.Requests.Should().ContainSingle().Subject;
        seen.Method.Should().Be("GET");
        seen.Path.Should().Be("/api/workflow/service-orders/o1");
        seen.Query.Should().Be("?x=1&y=a%20b");
        _kernel.Requests.Should().BeEmpty("a vertical route never falls through to the kernel's catch-all");
    }

    [Theory]
    [InlineData("/api/workflow/service-orders/o1")]
    [InlineData("/api/identity/me")]
    [InlineData("/api/registry/order-types")]
    [InlineData("/api/targets/ledger/neutral-operator/o1/reserve")]
    public async Task EveryOtherApiPath_ReachesTheKernel_AsItIs_AndNeverTheVertical(string path)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative(path));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _kernel.Requests.Should().ContainSingle().Which.Path.Should().Be(path);
        _neutralOperator.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/wms", "?service=WMS&request=GetMap")]
    [InlineData("/wfs", "?service=WFS&request=GetFeature")]
    public async Task TheOgcEndpoints_ReachTheKernel_WithoutAnyToken(string path, string query)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative(path + query));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = _kernel.Requests.Should().ContainSingle().Subject;
        seen.Path.Should().Be(path);
        seen.Query.Should().Be(query);
        seen.Headers.Should().NotContainKey("Authorization", "an anonymous tile request is proxied as it is: the gateway adds nothing");
        _neutralOperator.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/geoassets/features")]
    [InlineData("/api/ip-billing/x")]
    [InlineData("/api/field-service/x")]
    public async Task TheServicePrefixes_AreForwardedAsTheyAre(string path)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        await client.GetAsync(Relative(path));

        _kernel.Requests.Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public async Task TheGatewaysOwnHealthEndpoint_AnswersItself_AndIsNotProxied()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative("/healthz"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _kernel.Requests.Should().BeEmpty();
        _neutralOperator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnroutedPath_Is404_AndReachesNoBackend()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative("/not-a-route"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _kernel.Requests.Should().BeEmpty();
        _neutralOperator.Requests.Should().BeEmpty();
    }

    // ── Headers ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln")]
    [InlineData("Bearer abc/def+ghi==.QUJD-_x")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Authorization_IsForwardedByteForByte_OnTheVerticalAndTheKernelRoutes(string authorization)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        foreach (var path in new[] { VerticalRoute, "/api/workflow/service-orders/o1" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Relative(path));
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            await client.SendAsync(request);
        }

        _neutralOperator.Requests.Single().Header("Authorization").Should().Be(authorization);
        _kernel.Requests.Single().Header("Authorization").Should().Be(authorization);
    }

    [Fact]
    public async Task TheOrganizationHeaders_AreStripped_OnEveryRoute_AndOtherHeadersPass()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        foreach (var path in new[] { VerticalRoute, "/api/workflow/service-orders/o1", "/wms" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Relative(path));
            request.Headers.Add("X-GeoAssets-Organization-Id", "00000000-0000-4000-8000-000000000001");
            request.Headers.Add("X-Organization-Id", "00000000-0000-4000-8000-000000000002");
            request.Headers.Add("X-Custom", "kept");
            await client.SendAsync(request);
        }

        foreach (var seen in _neutralOperator.Requests.Concat(_kernel.Requests))
        {
            seen.Headers.Should().NotContainKey("X-GeoAssets-Organization-Id").And.NotContainKey("X-Organization-Id");
            seen.Header("X-Custom").Should().Be("kept");
        }

        _neutralOperator.Requests.Should().HaveCount(1);
        _kernel.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ASpoofedXForwardedFor_IsOverwritten_AndTheForwardedProtoAndHostAreTheGatewaysOwn()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(VerticalRoute));
        request.Headers.Add("X-Forwarded-For", "6.6.6.6");
        request.Headers.Add("X-Forwarded-Proto", "gopher");
        request.Headers.Add("X-Forwarded-Host", "evil.test");

        await client.SendAsync(request);

        var seen = _neutralOperator.Requests.Single();
        seen.Header("X-Forwarded-For").Should().NotContain("6.6.6.6");
        seen.Header("X-Forwarded-Proto").Should().Be("http");
        seen.Header("X-Forwarded-Host").Should().Be("localhost");
    }

    [Fact]
    public async Task TheTraceId_OfTheCallersTraceparent_IsPreservedDownstream_AcrossTheGatewayHop()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || source.Name.StartsWith("System.Net.Http", StringComparison.Ordinal)
                || string.Equals(source.Name, GatewayTelemetry.YarpActivitySourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(VerticalRoute));
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        await client.SendAsync(request);

        var downstream = _neutralOperator.Requests.Single().Header("traceparent");
        downstream.Should().NotBeNull();
        downstream!.Split('-')[1].Should().Be(traceId, "the gateway hop is a span of the caller's trace, not a new one");
    }

    // ── CORS ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VerticalRoute, "POST")]
    [InlineData("/api/workflow/service-orders", "POST")]
    public async Task APreflightFromAnAllowedOrigin_IsAnsweredAtTheGateway_AndNoBackendIsCalled(string path, string method)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, Relative(path));
        request.Headers.Add("Origin", GatewayFactory.WebOrigin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-tenant-domain");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GatewayFactory.WebOrigin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Single().Should().Contain(method);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().ContainEquivalentOf("authorization");
        _kernel.Requests.Should().BeEmpty();
        _neutralOperator.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(VerticalRoute)]
    [InlineData("/api/workflow/service-orders/o1")]
    public async Task AProxiedResponse_ForAnAllowedOrigin_CarriesExactlyOneAllowOrigin_TheGatewaysOwn_EvenWhenTheBackendSendsAWildcard(string path)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(path));
        request.Headers.Add("Origin", GatewayFactory.WebOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GatewayFactory.WebOrigin);
    }

    [Fact]
    public async Task TheBackendsOtherCorsHeaders_AreNotPassedThrough()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(VerticalRoute));
        request.Headers.Add("Origin", GatewayFactory.WebOrigin);

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        response.Headers.Contains("Access-Control-Expose-Headers").Should().BeFalse();
    }

    [Theory]
    [InlineData(VerticalRoute)]
    [InlineData("/api/workflow/service-orders/o1")]
    public async Task AnUnknownOrigin_GetsNoAllowOrigin_NotEvenTheBackendsWildcard(string path)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(path));
        request.Headers.Add("Origin", "https://evil.test");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task APreflightFromAnUnknownOrigin_IsNotGrantedAnything()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, Relative(VerticalRoute));
        request.Headers.Add("Origin", "https://evil.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task ARequestWithoutAnOrigin_CarriesNoCorsHeaders()
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Relative(VerticalRoute));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    // ── Rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheApi_IsRateLimited_429_WhenTheBucketIsExhausted_AndTheLimitedRequestNeverReachesABackend()
    {
        await using var factory = Gateway(("RateLimiting:TokenLimit", "2"), ("RateLimiting:TokensPerPeriod", "2"), ("RateLimiting:ReplenishmentPeriodSeconds", "3600"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "t");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) statuses.Add((await client.GetAsync(Relative(VerticalRoute))).StatusCode);

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests);
        _neutralOperator.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheHealthEndpoint_IsNeverRateLimited_AndDoesNotConsumeTheApisBucket()
    {
        await using var factory = Gateway(("RateLimiting:TokenLimit", "1"), ("RateLimiting:TokensPerPeriod", "1"), ("RateLimiting:ReplenishmentPeriodSeconds", "3600"));
        using var client = factory.CreateClient();

        var health = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) health.Add((await client.GetAsync(Relative("/healthz"))).StatusCode);
        var first = await client.GetAsync(Relative(VerticalRoute));
        var second = await client.GetAsync(Relative(VerticalRoute));

        health.Should().OnlyContain(status => status == HttpStatusCode.OK);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ARateLimitedResponse_StillCarriesTheCorsHeaders_SoTheBrowserCanReadIt()
    {
        await using var factory = Gateway(("RateLimiting:TokenLimit", "1"), ("RateLimiting:TokensPerPeriod", "1"), ("RateLimiting:ReplenishmentPeriodSeconds", "3600"));
        using var client = factory.CreateClient();
        await client.GetAsync(Relative(VerticalRoute));
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative(VerticalRoute));
        request.Headers.Add("Origin", GatewayFactory.WebOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GatewayFactory.WebOrigin);
    }
}
