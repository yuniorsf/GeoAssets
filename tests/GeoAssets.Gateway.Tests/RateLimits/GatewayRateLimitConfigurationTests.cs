using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static GeoAssets.Gateway.Tests.RateLimits.RateLimitTestGateway;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-393: the rate limiter's configuration — buckets, the legacy flat keys, and the trusted proxies' client address.</summary>
public sealed class GatewayRateLimitConfigurationTests : IAsyncLifetime
{
    private RecordingBackend _backend = null!;

    public async Task InitializeAsync() => _backend = await RecordingBackend.StartAsync("backend");

    public async Task DisposeAsync() => await _backend.DisposeAsync();

    private static ServiceProvider Services(Dictionary<string, string?> values, bool addLimiter = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddOptions();
        if (addLimiter) services.AddLogging().AddTenantRateLimiting(configuration);
        else services.AddGatewayTrustedProxies(configuration);
        return services.BuildServiceProvider();
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception.InnerException is not null) exception = exception.InnerException;
        return exception;
    }

    // ── Buckets ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheApiSection_SizesTheApiBucket_AndTheTilesSectionTheTilesBucket()
    {
        await using var factory = Create(_backend, [.. Bucket("Api", 2), .. Bucket("Tiles", 4)]);
        using var client = factory.CreateClient();

        var api = await BurstAsync(client, ApiPath, 3);
        var tiles = await BurstAsync(client, TilesPath, 5);

        api.Count(s => s == HttpStatusCode.OK).Should().Be(2);
        tiles.Count(s => s == HttpStatusCode.OK).Should().Be(4);
        tiles[^1].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheLegacyFlatKeys_OverrideTheApiSection_ButNotTheTilesSection()
    {
        await using var factory = Create(
            _backend,
            [.. Bucket("Api", 5), .. Bucket("Tiles", 3), ("RateLimiting:TokenLimit", "2"), ("RateLimiting:TokensPerPeriod", "1"), ("RateLimiting:ReplenishmentPeriodSeconds", "3600")]);
        using var client = factory.CreateClient();

        var api = await BurstAsync(client, ApiPath, 3);
        var tiles = await BurstAsync(client, TilesPath, 4);

        api.Count(s => s == HttpStatusCode.OK).Should().Be(2, "the flat TokenLimit wins over RateLimiting:Api:TokenLimit");
        tiles.Count(s => s == HttpStatusCode.OK).Should().Be(3);
    }

    [Fact]
    public async Task WithNothingConfigured_TheShippedDefaultsApply_AProductionSizedBurstIsAdmitted()
    {
        await using var factory = Create(_backend);
        using var client = factory.CreateClient();

        var api = await BurstAsync(client, ApiPath, 50);

        api.Should().AllBeEquivalentTo(HttpStatusCode.OK, "the shipped Api bucket holds 500");
    }

    [Theory]
    [InlineData("RateLimiting:Api:TokenLimit", "0")]
    [InlineData("RateLimiting:Api:TokensPerPeriod", "-1")]
    [InlineData("RateLimiting:Api:ReplenishmentPeriodSeconds", "0")]
    [InlineData("RateLimiting:Tiles:TokenLimit", "0")]
    [InlineData("RateLimiting:Tiles:TokensPerPeriod", "0")]
    [InlineData("RateLimiting:Tiles:ReplenishmentPeriodSeconds", "-5")]
    [InlineData("RateLimiting:TokenLimit", "0")]
    [InlineData("RateLimiting:ReplenishmentPeriodSeconds", "0")]
    public void AValueBelowOne_ThrowsAtStartup(string key, string value)
    {
        var act = () => Services(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value }, addLimiter: true);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("at least 1");
    }

    [Fact]
    public async Task AValueBelowOne_StopsTheGatewayFromStarting()
    {
        await using var factory = Create(_backend, ("RateLimiting:Api:TokenLimit", "0"));

        var act = () => Task.FromResult(factory.CreateClient());

        (await act.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => Unwrap(e).Message.Contains("at least 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonNumericValue_Throws()
    {
        var act = () => Services(new Dictionary<string, string?>(StringComparer.Ordinal) { ["RateLimiting:Tiles:TokenLimit"] = "lots" }, addLimiter: true);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Trusted proxies: services ─────────────────────────────────────────────

    [Fact]
    public void TrustedProxies_NothingConfigured_IsEmptyAndLeavesTheForwardedHeadersOptionsAlone()
    {
        using var provider = Services(new Dictionary<string, string?>(StringComparer.Ordinal));

        provider.GetRequiredService<TrustedProxySettings>().IsEmpty.Should().BeTrue();
        provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardedHeaders.Should().Be(ForwardedHeaders.None);
    }

    [Fact]
    public void TrustedProxies_AProxyAndANetwork_AreTheOnlyTrustedHops_ForXForwardedForOnly()
    {
        using var provider = Services(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RateLimiting:TrustedProxies:0"] = "10.1.2.3",
            ["RateLimiting:TrustedNetworks:0"] = "172.16.0.0/12",
        });

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor);
        options.KnownProxies.Should().Equal([IPAddress.Parse("10.1.2.3")], "the loopback default is cleared");
        options.KnownIPNetworks.Should().ContainSingle().Which.Should().Be(IPNetwork.Parse("172.16.0.0/12"));
        options.ForwardLimit.Should().BeNull();
    }

    [Fact]
    public void TrustedProxies_BlankEntries_AreSkipped()
    {
        using var provider = Services(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RateLimiting:TrustedProxies:0"] = "",
            ["RateLimiting:TrustedProxies:1"] = "  10.9.9.9 ",
            ["RateLimiting:TrustedNetworks:0"] = " ",
        });

        var settings = provider.GetRequiredService<TrustedProxySettings>();

        settings.Proxies.Should().Equal(IPAddress.Parse("10.9.9.9"));
        settings.Networks.Should().BeEmpty();
    }

    [Fact]
    public void TrustedProxies_OnlyBlankEntries_IsTheSameAsNone()
    {
        using var provider = Services(new Dictionary<string, string?>(StringComparer.Ordinal) { ["RateLimiting:TrustedProxies:0"] = "" });

        provider.GetRequiredService<TrustedProxySettings>().IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("RateLimiting:TrustedProxies:0", "not-an-ip")]
    [InlineData("RateLimiting:TrustedProxies:0", "10.0.0.0/8")]
    [InlineData("RateLimiting:TrustedNetworks:0", "10.0.0.1")]
    [InlineData("RateLimiting:TrustedNetworks:0", "10.0.0.0/99")]
    [InlineData("RateLimiting:TrustedNetworks:0", "nonsense/8")]
    public void TrustedProxies_AnInvalidEntry_ThrowsAtStartup_NamingTheEntry(string key, string value)
    {
        var act = () => Services(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(value);
    }

    // ── Trusted proxies: through the gateway ──────────────────────────────────

    [Fact]
    public async Task FromATrustedProxy_TheForwardedClientAddress_IsTheBucketKey()
    {
        await using var factory = Create(_backend, [.. Bucket("Api", 1), ("RateLimiting:TrustedProxies:0", "10.0.0.1")]);
        using var client = factory.CreateClient();

        var clientA = await BurstAsync(client, ApiPath, 2, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        var clientB = await BurstAsync(client, ApiPath, 1, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.6");

        clientA.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        clientB.Should().Equal([HttpStatusCode.OK], "two clients behind one load balancer are two callers");
    }

    [Fact]
    public async Task FromATrustedNetwork_TheForwardedClientAddress_IsTheBucketKey()
    {
        await using var factory = Create(_backend, [.. Bucket("Tiles", 1), ("RateLimiting:TrustedNetworks:0", "10.0.0.0/24")]);
        using var client = factory.CreateClient();

        var clientA = await BurstAsync(client, TilesPath, 2, remoteIp: "10.0.0.77", forwardedFor: "203.0.113.5");
        var clientB = await BurstAsync(client, TilesPath, 1, remoteIp: "10.0.0.78", forwardedFor: "203.0.113.6");

        clientA.Should().Equal(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        clientB.Should().Equal(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FromAnUntrustedPeer_XForwardedForIsIgnored_AndTheClientsShareThePeersBucket()
    {
        await using var factory = Create(_backend, [.. Bucket("Api", 1), ("RateLimiting:TrustedProxies:0", "10.0.0.1")]);
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 1, remoteIp: "198.51.100.9", forwardedFor: "203.0.113.5");
        var second = await BurstAsync(client, ApiPath, 1, remoteIp: "198.51.100.9", forwardedFor: "203.0.113.6");

        first.Should().Equal(HttpStatusCode.OK);
        second.Should().Equal([HttpStatusCode.TooManyRequests], "a spoofed header from an untrusted peer must not buy a fresh bucket");
    }

    [Fact]
    public async Task WithNoTrustedProxyConfigured_XForwardedForIsIgnored()
    {
        await using var factory = Create(_backend, Bucket("Api", 1));
        using var client = factory.CreateClient();

        var first = await BurstAsync(client, ApiPath, 1, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        var second = await BurstAsync(client, ApiPath, 1, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.6");

        factory.Services.GetRequiredService<TrustedProxySettings>().IsEmpty.Should().BeTrue();
        first.Should().Equal(HttpStatusCode.OK);
        second.Should().Equal(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task BehindATrustedProxy_TheBackendReceivesTheResolvedClientAddress_NeverAHeaderTheCallerChose()
    {
        await using var factory = Create(_backend, [.. Bucket("Api", 5), ("RateLimiting:TrustedProxies:0", "10.0.0.1")]);
        using var client = factory.CreateClient();

        await BurstAsync(client, ApiPath, 1, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        await BurstAsync(client, ApiPath, 1, remoteIp: "198.51.100.9", forwardedFor: "6.6.6.6");

        var seen = _backend.Requests;
        seen.Should().HaveCount(2);
        seen[0].Header("X-Forwarded-For").Should().Be("203.0.113.5");
        seen[1].Header("X-Forwarded-For").Should().Be("198.51.100.9").And.NotContain("6.6.6.6");
    }

    [Fact]
    public async Task FromATrustedProxy_AGarbageXForwardedFor_StillAnswers_NotA500()
    {
        await using var factory = Create(_backend, [.. Bucket("Api", 5), ("RateLimiting:TrustedProxies:0", "10.0.0.1")]);
        using var client = factory.CreateClient();

        var statuses = await BurstAsync(client, ApiPath, 1, remoteIp: "10.0.0.1", forwardedFor: "not-an-ip, , 999.1.1.1");

        statuses.Single().Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }
}
