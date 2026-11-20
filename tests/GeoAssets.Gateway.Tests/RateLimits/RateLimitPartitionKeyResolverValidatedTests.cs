using System.Net;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;
using static GeoAssets.Gateway.Tests.RateLimits.RateLimitTestGateway;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-398: the partition caller of a token-validation result — claims count only from a validated (or, unconfigured, decoded) token.</summary>
public sealed class RateLimitPartitionKeyResolverValidatedTests
{
    private const string OrgClaim = "org_id";
    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.9");
    private static readonly ClaimMapping WithOrganization = new() { OrganizationIdClaimTypes = [OrgClaim] };

    private static GatewayTokenValidation Validation(GatewayTokenValidationOutcome outcome, params (string Type, string Value)[] claims) =>
        new(outcome, [.. claims.Select(c => new Claim(c.Type, c.Value))]);

    [Theory]
    [InlineData("Valid")]
    [InlineData("Unvalidated")]
    public void Resolve_AUsableOutcome_PrefersTheOrganizationClaim(string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        var caller = RateLimitPartitionKeyResolver.Resolve(Validation(outcome, (OrgClaim, "acme"), ("oid", "alice")), WithOrganization, Client);

        caller.Should().Be(new PartitionCaller("org:acme", "org", outcome));
    }

    [Theory]
    [InlineData("Valid")]
    [InlineData("Unvalidated")]
    public void Resolve_AUsableOutcomeWithoutAnOrganizationClaim_UsesOidThenSub(string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        RateLimitPartitionKeyResolver.Resolve(Validation(outcome, ("sub", "s"), ("oid", "alice")), WithOrganization, Client)
            .Should().Be(new PartitionCaller("sub:alice", "sub", outcome));
        RateLimitPartitionKeyResolver.Resolve(Validation(outcome, ("sub", "s")), ClaimMapping.EntraDefault, Client)
            .Should().Be(new PartitionCaller("sub:s", "sub", outcome));
    }

    [Theory]
    [InlineData("Valid")]
    [InlineData("Unvalidated")]
    public void Resolve_AUsableOutcomeWithNoUsableClaim_UsesTheAddress(string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        RateLimitPartitionKeyResolver.Resolve(Validation(outcome, ("name", "alice")), WithOrganization, Client)
            .Should().Be(new PartitionCaller("ip:203.0.113.9", "ip", outcome));
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("Unavailable")]
    [InlineData("Absent")]
    public void Resolve_AnUnusableOutcome_IgnoresItsClaimsAndUsesTheAddress(string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        var caller = RateLimitPartitionKeyResolver.Resolve(Validation(outcome, (OrgClaim, "acme"), ("oid", "victim")), WithOrganization, Client);

        caller.Should().Be(new PartitionCaller("ip:203.0.113.9", "ip", outcome));
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("Unavailable")]
    [InlineData("Absent")]
    [InlineData("Valid")]
    [InlineData("Unvalidated")]
    public void Resolve_NoClaimAndNoAddress_IsUnresolved(string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        var caller = RateLimitPartitionKeyResolver.Resolve(Validation(outcome), WithOrganization, null);

        caller.Should().Be(new PartitionCaller(RateLimitPartitionKeyResolver.UnresolvedPartitionKey, "unresolved", outcome));
    }

    [Fact]
    public void Resolve_AnAbsentValidationSingleton_UsesTheAddress()
    {
        RateLimitPartitionKeyResolver.Resolve(GatewayTokenValidation.Absent, WithOrganization, Client).Key.Should().Be("ip:203.0.113.9");
    }

    [Fact]
    public void Resolve_AnIpv4MappedAddress_IsKeyedAsIpv4()
    {
        var caller = RateLimitPartitionKeyResolver.Resolve(GatewayTokenValidation.Absent, WithOrganization, IPAddress.Parse("::ffff:203.0.113.9"));

        caller.Key.Should().Be("ip:203.0.113.9");
    }

    [Theory]
    [InlineData("valid", "Valid")]
    [InlineData("invalid", "Invalid")]
    [InlineData("absent", "Absent")]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("unvalidated", "Unvalidated")]
    public void ToName_IsTheLowerCaseMetricValue(string expected, string outcomeName)
    {
        var outcome = Enum.Parse<GatewayTokenValidationOutcome>(outcomeName);
        outcome.ToName().Should().Be(expected);
    }

    // ── ReadBearerToken ──────────────────────────────────────────────────────

    private static string? ReadBearer(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        return RateLimitPartitionKeyResolver.ReadBearerToken(context.Request);
    }

    [Theory]
    [InlineData("Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("bearer abc", "abc")]
    [InlineData("BEARER abc", "abc")]
    [InlineData("Bearer   abc  ", "abc")]
    public void ReadBearerToken_ABearerHeader_IsTheTrimmedValue(string header, string expected)
    {
        ReadBearer(header).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic YWxpY2U6cHc=")]
    [InlineData("Bearer")]
    [InlineData("Bearer    ")]
    public void ReadBearerToken_NoBearerValue_IsNull(string? header)
    {
        ReadBearer(header).Should().BeNull();
    }

    [Fact]
    public void ReadBearerToken_AGeneratedToken_RoundTrips()
    {
        var token = Token(("oid", "alice"));

        ReadBearer("Bearer " + token).Should().Be(token);
    }
}
