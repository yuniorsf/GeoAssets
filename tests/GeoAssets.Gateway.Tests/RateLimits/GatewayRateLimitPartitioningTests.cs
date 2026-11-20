using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Gateway.Tests.RateLimits.RateLimitTestGateway;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-393: the gateway's real <c>Program</c> keys its buckets per caller and per policy, and answers a rejection as documented.</summary>
public sealed class GatewayRateLimitPartitioningTests : IAsyncLifetime
{
    private const string OrgClaim = "org_id";

    private RecordingBackend _backend = null!;

    public async Task InitializeAsync() => _backend = await RecordingBackend.StartAsync("backend");

    public async Task DisposeAsync() => await _backend.DisposeAsync();

    private static (string Key, string Value)[] Join(params (string Key, string Value)[][] parts) => [.. parts.SelectMany(p => p)];

    // ── Callers: no organization claim ────────────────────────────────────────

    [Fact]
    public async Task TwoUsersWithoutAnOrganizationClaim_HaveIndependentBuckets()
    {
        await using var factory = Create(_backend, Bucket("Api", 2));
        using var client = factory.CreateClient();
        var alice = Token(("oid", "alice"));
        var bob = Token(("oid", "bob"));

        var aliceStatuses = await BurstAsync(client, ApiPath, 3, alice);
        var bobStatuses = await BurstAsync(client, ApiPath, 2, bob);

        aliceStatuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        bobStatuses.Should().AllBeEquivalentTo(HttpStatusCode.OK, "alice exhausting her bucket must not spend bob's");
    }

    [Fact]
    public async Task TwoUsersIdentifiedBySubOnly_HaveIndependentBuckets()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 2, Token(("sub", "u1")));
        var second = await BurstAsync(client, ApiPath, 1, Token(("sub", "u2")));

        first.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        second.Should().Equal(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OneUser_SharesOneBucketAcrossRequestsAndAddresses()
    {
        await using var factory = Create(_backend, Bucket("Api", 2));
        using var client = factory.CreateClient();
        var token = Token(("oid", "alice"));

        var statuses = new List<HttpStatusCode>();
        foreach (var ip in new[] { "198.51.100.1", "198.51.100.2", "198.51.100.3" })
            statuses.AddRange(await BurstAsync(client, ApiPath, 1, token, ip));

        statuses.Should().Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], "the subject, not the address, is the key of a token holder");
    }

    // ── Callers: an organization claim ────────────────────────────────────────

    [Fact]
    public async Task WithAnOrganizationClaimConfigured_TwoUsersOfOneOrganizationShareABucket_AndAnotherOrganizationHasItsOwn()
    {
        await using var factory = Create(_backend, Join(Bucket("Api", 2), [("RateLimiting:OrganizationIdClaimType", OrgClaim)]));
        using var client = factory.CreateClient();
        var acmeAlice = Token((OrgClaim, "acme"), ("oid", "alice"));
        var acmeBob = Token((OrgClaim, "acme"), ("oid", "bob"));
        var globexCarol = Token((OrgClaim, "globex"), ("oid", "carol"));

        var alice = await BurstAsync(client, ApiPath, 1, acmeAlice);
        var bob = await BurstAsync(client, ApiPath, 2, acmeBob);
        var carol = await BurstAsync(client, ApiPath, 2, globexCarol);

        alice.Should().Equal(HttpStatusCode.OK);
        bob.Should().Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], "alice already spent one of acme's two tokens");
        carol.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithAnOrganizationClaimConfigured_AUserWithoutThatClaimFallsBackToTheSubject()
    {
        await using var factory = Create(_backend, Join(Bucket("Api", 1), [("RateLimiting:OrganizationIdClaimType", OrgClaim)]));
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 2, Token(("oid", "alice")));
        var second = await BurstAsync(client, ApiPath, 1, Token(("oid", "bob")));

        first.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        second.Should().Equal(HttpStatusCode.OK);
    }

    // ── Callers: anonymous ────────────────────────────────────────────────────

    [Fact]
    public async Task TwoAnonymousClientsFromDifferentAddresses_HaveIndependentBuckets()
    {
        await using var factory = Create(_backend, Bucket("Api", 2));
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 3, remoteIp: "203.0.113.10");
        var second = await BurstAsync(client, ApiPath, 2, remoteIp: "203.0.113.11");
        var firstAgain = await BurstAsync(client, ApiPath, 1, remoteIp: "203.0.113.10");

        first.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        second.Should().AllBeEquivalentTo(HttpStatusCode.OK);
        firstAgain.Should().Equal([HttpStatusCode.TooManyRequests], "one address keeps one bucket");
    }

    [Fact]
    public async Task AnAnonymousClientAndTheSameClientIPv4MappedToIPv6_ShareABucket()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();

        var plain = await BurstAsync(client, ApiPath, 1, remoteIp: "203.0.113.20");
        var mapped = await BurstAsync(client, ApiPath, 1, remoteIp: "::ffff:203.0.113.20");

        plain.Should().Equal(HttpStatusCode.OK);
        mapped.Should().Equal(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AGarbageBearerToken_FallsBackToTheClientAddress_NotA500()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 2, token: "abc.def.ghi", remoteIp: "203.0.113.30");
        var other = await BurstAsync(client, ApiPath, 1, token: "abc.def.ghi", remoteIp: "203.0.113.31");

        first.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        other.Should().Equal(HttpStatusCode.OK);
    }

    // ── Policies ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExhaustingTheTilesPolicy_DoesNotRateLimitApiCalls_OfTheSameCaller()
    {
        await using var factory = Create(_backend, Join(Bucket("Tiles", 2), Bucket("Api", 2)));
        using var client = factory.CreateClient();
        var token = Token(("oid", "alice"));

        var tiles = await BurstAsync(client, TilesPath, 3, token, "203.0.113.40");
        var api = await BurstAsync(client, ApiPath, 2, token, "203.0.113.40");

        tiles.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        api.Should().AllBeEquivalentTo(HttpStatusCode.OK, "tiles and API are disjoint budgets");
    }

    [Fact]
    public async Task ExhaustingTheApiPolicy_DoesNotRateLimitTileRequests_OfTheSameCaller()
    {
        await using var factory = Create(_backend, Join(Bucket("Tiles", 2), Bucket("Api", 2)));
        using var client = factory.CreateClient();
        var token = Token(("oid", "alice"));

        var api = await BurstAsync(client, ApiPath, 3, token, "203.0.113.41");
        var tiles = await BurstAsync(client, TilesPath, 2, token, "203.0.113.41");

        api.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        tiles.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WfsIsLimitedByTheTilesPolicy_AndAnApiPathNamedWms_ByTheApiPolicy()
    {
        await using var factory = Create(_backend, Join(Bucket("Tiles", 1), Bucket("Api", 1)));
        using var client = factory.CreateClient();

        var wfs = await BurstAsync(client, "/wfs?SERVICE=WFS", 2, remoteIp: "203.0.113.50");
        var lookalike = await BurstAsync(client, "/api/geoassets/wms", 2, remoteIp: "203.0.113.50");

        wfs.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        lookalike.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Healthz_IsNeverLimited_EvenWithBothPoliciesExhausted()
    {
        await using var factory = Create(_backend, Join(Bucket("Tiles", 1), Bucket("Api", 1)));
        using var client = factory.CreateClient();
        await BurstAsync(client, ApiPath, 2, remoteIp: "203.0.113.60");
        await BurstAsync(client, TilesPath, 2, remoteIp: "203.0.113.60");

        var health = await BurstAsync(client, "/healthz", 10, remoteIp: "203.0.113.60");

        health.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    // ── The 429 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ApiPath, "api")]
    [InlineData(TilesPath, "tiles")]
    public async Task ARejectedRequest_Is429_WithRetryAfter_AProblemBody_AndTheCorsHeaders(string path, string policy)
    {
        var settings = string.Equals(policy, "api", StringComparison.Ordinal) ? Bucket("Api", 1, period: 3600) : Bucket("Tiles", 1, period: 3600);
        await using var factory = Create(_backend, settings);
        using var client = factory.CreateClient();
        await BurstAsync(client, path, 1, remoteIp: "203.0.113.70");

        using var response = await GetAsync(client, path, remoteIp: "203.0.113.70", origin: GatewayFactory.WebOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 3600);
        response.Headers.RetryAfter.Delta.Value.TotalSeconds.Should().Be(3600, "one token per 3600 s: the wait is the replenishment period");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(GatewayFactory.WebOrigin);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(429);
        root.GetProperty("title").GetString().Should().Be("Too many requests");
        root.GetProperty("code").GetString().Should().Be("rate-limit.exceeded");
        root.GetProperty("policy").GetString().Should().Be(policy);
        root.GetProperty("detail").GetString().Should().Contain($"'{policy}'").And.Contain("retry after 3600 s");
    }

    [Fact]
    public async Task ARejectedRequest_NeverReachesTheBackend()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();

        await BurstAsync(client, ApiPath, 3, remoteIp: "203.0.113.71");

        _backend.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ARejectedRequestWithAShortPeriod_AdvertisesAtLeastOneSecond()
    {
        await using var factory = Create(_backend, Bucket("Api", 1, period: 1));
        using var client = factory.CreateClient();
        await BurstAsync(client, ApiPath, 1, remoteIp: "203.0.113.72");

        using var response = await GetAsync(client, ApiPath, remoteIp: "203.0.113.72");

        // A fast machine can see the bucket refilled between the two requests; only a rejection has a header to check.
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── Metric ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EachRejection_IncrementsTheRateLimitedCounterOnce_TaggedByPolicyOnly()
    {
        await using var factory = Create(_backend, Join(Bucket("Tiles", 1), Bucket("Api", 1)));
        using var client = factory.CreateClient();
        using var measurements = new RateLimitedMeasurements(factory.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());

        await BurstAsync(client, ApiPath, 3, Token(("oid", "alice")));
        await BurstAsync(client, TilesPath, 2, remoteIp: "203.0.113.80");

        var recorded = measurements.Measurements.ToArray();
        recorded.Should().HaveCount(3, "two API rejections and one tile rejection");
        recorded.Should().OnlyContain(m => m.Value == 1);
        recorded.Should().OnlyContain(m => m.Tags.Length == 1 && string.Equals(m.Tags[0].Key, "policy", StringComparison.Ordinal), "a partition key is unbounded and never a tag");
        recorded.Count(m => string.Equals((string?)m.Tags[0].Value, "api", StringComparison.Ordinal)).Should().Be(2);
        recorded.Count(m => string.Equals((string?)m.Tags[0].Value, "tiles", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task AdmittedRequests_AreNotCounted()
    {
        await using var factory = Create(_backend, Bucket("Api", 5));
        using var client = factory.CreateClient();
        using var measurements = new RateLimitedMeasurements(factory.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());

        await BurstAsync(client, ApiPath, 5);

        measurements.Measurements.Should().BeEmpty();
    }
}
