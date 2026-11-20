using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-398: the gateway's token validator in isolation — signature, issuer and lifetime against a fake tenant, bounded metadata traffic.</summary>
public sealed class GatewayTokenValidatorTests : IDisposable
{
    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(30);

    private readonly OidcTestIdentityProvider _tenant = new();
    private readonly FakeTimeProvider _time = new(TimeProvider.System.GetUtcNow());
    private readonly HttpClient _http;

    public GatewayTokenValidatorTests() => _http = _tenant.CreateHttpClient();

    public void Dispose()
    {
        _http.Dispose();
        _tenant.Dispose();
    }

    private GatewayTokenValidator Configured(TimeSpan? clockSkew = null)
    {
        var options = new GatewayTokenValidationOptions(OidcTestIdentityProvider.MetadataAddress, clockSkew ?? TimeSpan.FromMinutes(5), Backoff);
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            OidcTestIdentityProvider.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_http) { RequireHttps = false });
        return new GatewayTokenValidator(options, manager, _time, NullLogger<GatewayTokenValidator>.Instance);
    }

    private static GatewayTokenValidator Unconfigured() =>
        new(new GatewayTokenValidationOptions(null, TimeSpan.FromMinutes(5), Backoff), null, TimeProvider.System, NullLogger<GatewayTokenValidator>.Instance);

    private string Mint(string subject = "alice", bool foreignKey = false, TimeSpan? notBefore = null, TimeSpan? expires = null, string issuer = OidcTestIdentityProvider.Issuer, string? audience = null, bool reuseKeyId = false) =>
        _tenant.Mint(_time.GetUtcNow(), subject, foreignKey, notBefore, expires, issuer, audience, reuseKeyId);

    private static string? ClaimValue(GatewayTokenValidation validation, string type) =>
        validation.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.Ordinal))?.Value;

    // ── Valid ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_AValidToken_IsValidWithItsRawClaims()
    {
        var token = _tenant.Mint(
            _time.GetUtcNow(),
            new Dictionary<string, object>(StringComparer.Ordinal) { ["oid"] = "alice-oid", ["sub"] = "alice-sub" });

        var result = await Configured().ValidateAsync(token, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Valid);
        ClaimValue(result, "oid").Should().Be("alice-oid");
        ClaimValue(result, "sub").Should().Be("alice-sub");
    }

    [Fact]
    public async Task ValidateAsync_AValidToken_ReadsTheMetadataAndTheKeysOnce()
    {
        var validator = Configured();

        for (var i = 0; i < 20; i++) await validator.ValidateAsync(Mint($"user-{i}"), CancellationToken.None);

        _tenant.Handler.MetadataFetches.Should().Be(1);
        _tenant.Handler.JwksFetches.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_ATokenForAnyAudience_IsStillValid()
    {
        var result = await Configured().ValidateAsync(Mint(audience: "api://some-other-api"), CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AnExpiredTokenWithinTheClockSkew_IsValid()
    {
        var token = Mint(notBefore: TimeSpan.FromHours(-1), expires: TimeSpan.FromMinutes(-2));

        var result = await Configured(TimeSpan.FromMinutes(5)).ValidateAsync(token, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Valid);
    }

    // ── Invalid ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_ATokenSignedByAnotherKey_IsInvalidWithNoClaims(bool reuseKeyId)
    {
        var result = await Configured().ValidateAsync(Mint("victim", foreignKey: true, reuseKeyId: reuseKeyId), CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_RepeatedTokensOfAnUnknownKey_RefetchTheMetadataAtMostOnce()
    {
        var validator = Configured();
        await validator.ValidateAsync(Mint(), CancellationToken.None);
        var before = _tenant.Handler.TotalFetches;

        for (var i = 0; i < 10; i++)
            (await validator.ValidateAsync(Mint("victim", foreignKey: true), CancellationToken.None)).Outcome.Should().Be(GatewayTokenValidationOutcome.Invalid);

        (_tenant.Handler.MetadataFetches).Should().BeLessThanOrEqualTo(2, "a key refresh is bounded by the configuration manager's refresh interval");
        (_tenant.Handler.TotalFetches - before).Should().BeLessThanOrEqualTo(2, "an attacker's unknown key must not turn into a fetch per request");
    }

    [Fact]
    public async Task ValidateAsync_AnExpiredToken_IsInvalid()
    {
        var token = Mint(notBefore: TimeSpan.FromHours(-2), expires: TimeSpan.FromHours(-1));

        var result = await Configured().ValidateAsync(token, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_ATokenNotYetValid_IsInvalid()
    {
        var token = Mint(notBefore: TimeSpan.FromHours(1), expires: TimeSpan.FromHours(2));

        var result = await Configured().ValidateAsync(token, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_AWrongIssuer_IsInvalid()
    {
        var result = await Configured().ValidateAsync(Mint(issuer: "https://evil.test/tenant/v2.0"), CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Invalid);
    }

    // ── Absent ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task ValidateAsync_NothingDecodable_IsAbsentWithoutAnyMetadataFetch(string? raw)
    {
        var result = await Configured().ValidateAsync(raw, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Absent);
        result.Claims.Should().BeEmpty();
        _tenant.Handler.TotalFetches.Should().Be(0);
    }

    // ── Unavailable ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_TheMetadataIsDown_IsUnavailableAndBacksOffThenRetriesAndRecovers()
    {
        var validator = Configured();
        _tenant.Handler.Down = true;

        var first = await validator.ValidateAsync(Mint(), CancellationToken.None);
        var afterFirst = _tenant.Handler.TotalFetches;
        var second = await validator.ValidateAsync(Mint(), CancellationToken.None);
        _time.Advance(Backoff - TimeSpan.FromSeconds(1));
        var third = await validator.ValidateAsync(Mint(), CancellationToken.None);

        first.Outcome.Should().Be(GatewayTokenValidationOutcome.Unavailable);
        first.Claims.Should().BeEmpty();
        afterFirst.Should().BeGreaterThan(0);
        second.Outcome.Should().Be(GatewayTokenValidationOutcome.Unavailable);
        third.Outcome.Should().Be(GatewayTokenValidationOutcome.Unavailable);
        _tenant.Handler.TotalFetches.Should().Be(afterFirst, "within the backoff no further fetch is made");

        _time.Advance(TimeSpan.FromSeconds(2));
        var fourth = await validator.ValidateAsync(Mint(), CancellationToken.None);

        fourth.Outcome.Should().Be(GatewayTokenValidationOutcome.Unavailable);
        _tenant.Handler.TotalFetches.Should().BeGreaterThan(afterFirst, "after the backoff one fetch is tried again");
        _tenant.Handler.TotalFetches.Should().BeLessThanOrEqualTo(afterFirst * 2, "a single retry, not a burst");

        _tenant.Handler.Down = false;
        _time.Advance(Backoff + TimeSpan.FromSeconds(1));
        (await validator.ValidateAsync(Mint(), CancellationToken.None)).Outcome.Should().Be(GatewayTokenValidationOutcome.Valid);
    }

    // ── Not configured ───────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_NotConfigured_DecodesWithoutValidating()
    {
        var validator = Unconfigured();
        var forged = Mint("victim", foreignKey: true, notBefore: TimeSpan.FromHours(-2), expires: TimeSpan.FromHours(-1));

        var result = await validator.ValidateAsync(forged, CancellationToken.None);

        validator.IsConfigured.Should().BeFalse();
        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Unvalidated);
        ClaimValue(result, "oid").Should().Be("victim");
        _tenant.Handler.TotalFetches.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task ValidateAsync_NotConfiguredAndUndecodable_IsAbsent(string? raw)
    {
        var result = await Unconfigured().ValidateAsync(raw, CancellationToken.None);

        result.Outcome.Should().Be(GatewayTokenValidationOutcome.Absent);
    }

    [Fact]
    public void IsConfigured_WithAConfigurationManager_IsTrue()
    {
        Configured().IsConfigured.Should().BeTrue();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ACancelledCaller_PropagatesTheCancellationAndIsNotRememberedAsAFailure()
    {
        var validator = Configured();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = async () => await validator.ValidateAsync(Mint(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        (await validator.ValidateAsync(Mint(), CancellationToken.None)).Outcome.Should().Be(GatewayTokenValidationOutcome.Valid, "a caller's cancellation is not a metadata outage");
    }
}
