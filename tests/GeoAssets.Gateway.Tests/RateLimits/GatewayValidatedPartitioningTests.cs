using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Gateway.Tests.RateLimits.RateLimitTestGateway;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>
/// XD01-398: through the gateway's real <c>Program</c>, a bucket is keyed by a token's subject only when the token's
/// signature, issuer and lifetime validate against the tenant's keys; any other token falls back to the client address, and
/// no request is ever rejected because of its token.
/// </summary>
public sealed class GatewayValidatedPartitioningTests : IAsyncLifetime, IDisposable
{
    private static readonly string[] BoundedTags = ["policy", "source", "validation"];
    private const string VictimIp = "198.51.100.200";

    private readonly OidcTestIdentityProvider _tenant = new();
    private RecordingBackend _backend = null!;

    public async Task InitializeAsync() => _backend = await RecordingBackend.StartAsync("backend");

    public async Task DisposeAsync()
    {
        await _backend.DisposeAsync();
    }

    public void Dispose() => _tenant.Dispose();

    private GatewayFactory Validating(params (string Key, string Value)[] more) =>
        Create(
            _backend,
            services => services.AddHttpClient(GatewayTokenValidator.MetadataHttpClientName).ConfigurePrimaryHttpMessageHandler(() => _tenant.Handler),
            [("AzureAdCiam:MetadataAddress", OidcTestIdentityProvider.MetadataAddress), .. more]);

    private string Valid(string subject) => _tenant.Mint(TimeProvider.System.GetUtcNow(), subject);

    public static TheoryData<string> Forgeries => new() { "foreign-key", "foreign-key-with-known-kid", "expired", "wrong-issuer" };

    private string Forged(string kind, string subject)
    {
        var now = TimeProvider.System.GetUtcNow();
        return kind switch
        {
            "foreign-key" => _tenant.Mint(now, subject, foreignKey: true),
            "foreign-key-with-known-kid" => _tenant.Mint(now, subject, foreignKey: true, reuseKeyId: true),
            "expired" => _tenant.Mint(now, subject, notBefore: TimeSpan.FromHours(-3), expires: TimeSpan.FromHours(-2)),
            "wrong-issuer" => _tenant.Mint(now, subject, issuer: "https://evil.test/tenant/v2.0"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    // ── A validated token keys by its subject ────────────────────────────────

    [Fact]
    public async Task AValidlySignedToken_SharesOneBucketAcrossAddresses()
    {
        await using var factory = Validating(Bucket("Api", 2));
        using var client = factory.CreateClient();
        var token = Valid("alice");

        var statuses = new List<HttpStatusCode>();
        foreach (var ip in new[] { "198.51.100.1", "198.51.100.2", "198.51.100.3" })
            statuses.AddRange(await BurstAsync(client, ApiPath, 1, token, ip));

        statuses.Should().Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], "a validated subject, not the address, is the key");
    }

    [Fact]
    public async Task TwoValidlySignedSubjects_HaveIndependentBuckets()
    {
        await using var factory = Validating(Bucket("Api", 1));
        using var client = factory.CreateClient();

        var alice = await BurstAsync(client, ApiPath, 2, Valid("alice"), "198.51.100.1");
        var bob = await BurstAsync(client, ApiPath, 1, Valid("bob"), "198.51.100.1");

        alice.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        bob.Should().Equal(HttpStatusCode.OK);
    }

    // ── A forged token cannot spend the victim's bucket ──────────────────────

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task AForgedTokenNamingTheVictim_FallsBackToTheClientAddress_AndLeavesTheVictimsBucketAlone(string forgery)
    {
        await using var factory = Validating(Bucket("Api", 2));
        using var client = factory.CreateClient();
        var forged = Forged(forgery, "victim");

        var attacker = await BurstAsync(client, ApiPath, 4, forged, "203.0.113.66");
        var victim = await BurstAsync(client, ApiPath, 3, Valid("victim"), VictimIp);

        attacker.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests);
        victim.Should().Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], "the victim's own full bucket is intact");
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task AForgedToken_AndTheSubjectsOwnToken_DoNotShareABucket(string forgery)
    {
        await using var factory = Validating(Bucket("Api", 1));
        using var client = factory.CreateClient();

        var viaForgery = await BurstAsync(client, ApiPath, 1, Forged(forgery, "victim"), "203.0.113.66");
        var viaSubject = await BurstAsync(client, ApiPath, 1, Valid("victim"), "198.51.100.77");

        viaForgery.Should().Equal(HttpStatusCode.OK);
        viaSubject.Should().Equal([HttpStatusCode.OK], "the forged request spent the attacker's address bucket, not the subject's");
    }

    // ── The gateway never rejects because of a token ─────────────────────────

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task AForgedToken_GetsTheBackendsAnswer(string forgery)
    {
        await using var factory = Validating();
        using var client = factory.CreateClient();

        var statuses = await BurstAsync(client, ApiPath, 3, Forged(forgery, "victim"), "203.0.113.66");

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.OK);
        _backend.Requests.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task AGarbageOrBlankToken_GetsTheBackendsAnswer(string token)
    {
        await using var factory = Validating();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiPath, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.Add(RemoteAddressHeader, "203.0.113.67");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _tenant.Handler.TotalFetches.Should().Be(0, "an undecodable token costs no metadata fetch");
    }

    [Fact]
    public async Task ARequestWithoutAToken_GetsTheBackendsAnswer()
    {
        await using var factory = Validating();
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, ApiPath, remoteIp: "203.0.113.68");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheMetadataBeingDown_StillServesRequests_KeyedByAddress()
    {
        _tenant.Handler.Down = true;
        await using var factory = Validating(Bucket("Api", 2));
        using var client = factory.CreateClient();
        var token = Valid("alice");

        var statuses = await BurstAsync(client, ApiPath, 3, token, "203.0.113.69");
        var otherAddress = await BurstAsync(client, ApiPath, 1, token, "203.0.113.70");

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        otherAddress.Should().Equal([HttpStatusCode.OK], "with no keys the token is not trusted, so the address keys the bucket");
    }

    // ── Performance ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ManyRequestsWithValidTokens_FetchTheMetadataAndTheKeysExactlyOnce()
    {
        await using var factory = Validating(Bucket("Api", 1000));
        using var client = factory.CreateClient();

        for (var i = 0; i < 50; i++)
        {
            using var response = await GetAsync(client, ApiPath, Valid($"user-{i % 5}"), $"198.51.100.{i + 1}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _tenant.Handler.MetadataFetches.Should().Be(1);
        _tenant.Handler.JwksFetches.Should().Be(1);
    }

    // ── Telemetry ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryRequest_CountsItsPartitionWithSourceAndValidationTags_AndNeverTheKey()
    {
        await using var factory = Validating();
        using var client = factory.CreateClient();
        using var measurements = new PartitionMeasurements(factory.Services.GetRequiredService<IMeterFactory>());

        await GetAsync(client, ApiPath, Valid("alice"), "198.51.100.1");
        await GetAsync(client, ApiPath, Forged("foreign-key", "victim"), "198.51.100.2");
        await GetAsync(client, ApiPath, remoteIp: "198.51.100.3");

        var recorded = measurements.Measurements.ToArray();
        recorded.Should().HaveCount(3);
        recorded.Should().OnlyContain(m => m.Value == 1);
        recorded.Should().OnlyContain(m => m.Tags.Keys.All(k => BoundedTags.Contains(k)), "a partition key is unbounded and never a tag");
        recorded.Select(m => m.Tags["source"] + "/" + m.Tags["validation"]).Order(StringComparer.Ordinal).Should().Equal("ip/absent", "ip/invalid", "sub/valid");
        recorded.Should().OnlyContain(m => m.Tags["policy"] == "api");
    }

    [Fact]
    public async Task AnUnavailableTenant_IsCountedAsUnavailable()
    {
        _tenant.Handler.Down = true;
        await using var factory = Validating();
        using var client = factory.CreateClient();
        using var measurements = new PartitionMeasurements(factory.Services.GetRequiredService<IMeterFactory>());

        await GetAsync(client, ApiPath, Valid("alice"), "198.51.100.1");

        measurements.Measurements.Single().Tags.Should().Contain("validation", "unavailable").And.Contain("source", "ip");
    }

    // ── Not configured: today's behaviour ────────────────────────────────────

    [Fact]
    public async Task NotConfigured_ADecodedTokenStillKeysBySubject_AndIsCountedUnvalidated()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();
        using var measurements = new PartitionMeasurements(factory.Services.GetRequiredService<IMeterFactory>());
        var token = Token(("oid", "alice"));

        var first = await BurstAsync(client, ApiPath, 1, token, "198.51.100.1");
        var second = await BurstAsync(client, ApiPath, 1, token, "198.51.100.2");
        var other = await BurstAsync(client, ApiPath, 1, Token(("oid", "bob")), "198.51.100.2");

        first.Should().Equal(HttpStatusCode.OK);
        second.Should().Equal(HttpStatusCode.TooManyRequests);
        other.Should().Equal(HttpStatusCode.OK);
        measurements.Measurements.Select(m => m.Tags["source"] + "/" + m.Tags["validation"]).Should().OnlyContain(t => t == "sub/unvalidated");
        _tenant.Handler.TotalFetches.Should().Be(0);
    }

    private sealed class PartitionMeasurements : IDisposable
    {
        private readonly MeterListener _listener = new();

        public PartitionMeasurements(IMeterFactory scope)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, scope)
                    && string.Equals(instrument.Meter.Name, GatewayTelemetry.MeterName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, GatewayTelemetry.RateLimitPartitionCounterName, StringComparison.Ordinal))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
                Measurements.Add((value, tags.ToArray().ToDictionary(t => t.Key, t => (string?)t.Value, StringComparer.Ordinal))));
            _listener.Start();
        }

        public ConcurrentBag<(long Value, Dictionary<string, string?> Tags)> Measurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }
}
