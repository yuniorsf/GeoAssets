using System.Net;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;
using static GeoAssets.Gateway.Tests.RateLimits.RateLimitTestGateway;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-393: the partition key — organization claim, then subject, then client address, else "unresolved"; bounded; never throws.</summary>
public sealed class RateLimitPartitionKeyResolverTests
{
    private const string OrgClaim = "org_id";

    private static readonly ClaimMapping WithOrganization = new() { OrganizationIdClaimTypes = [OrgClaim] };

    private static string Resolve(string? authorization, string? remoteIp, ClaimMapping? mapping = null)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        if (remoteIp is not null) context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return RateLimitPartitionKeyResolver.Resolve(context.Request, mapping ?? WithOrganization);
    }

    private static string Bearer(params (string Type, string Value)[] claims) => "Bearer " + Token(claims);

    [Fact]
    public void Resolve_OrganizationClaimSubjectAndAddress_PrefersTheOrganization()
    {
        Resolve(Bearer((OrgClaim, "acme"), ("oid", "alice"), ("sub", "s")), "203.0.113.1").Should().Be("org:acme");
    }

    [Fact]
    public void Resolve_NoOrganizationClaimConfigured_UsesTheSubjectEvenWhenTheTokenHasAnOrgClaim()
    {
        Resolve(Bearer((OrgClaim, "acme"), ("oid", "alice")), "203.0.113.1", ClaimMapping.EntraDefault).Should().Be("sub:alice");
    }

    [Fact]
    public void Resolve_ABlankOrganizationClaim_FallsThroughToTheSubject()
    {
        Resolve(Bearer((OrgClaim, "  "), ("sub", "s1")), "203.0.113.1").Should().Be("sub:s1");
    }

    [Fact]
    public void Resolve_OidAndSub_PrefersOid()
    {
        Resolve(Bearer(("sub", "the-sub"), ("oid", "the-oid")), null).Should().Be("sub:the-oid");
    }

    [Fact]
    public void Resolve_SubOnly_UsesSub()
    {
        Resolve(Bearer(("sub", "the-sub")), null).Should().Be("sub:the-sub");
    }

    [Fact]
    public void Resolve_ATokenWithNoUsableClaim_UsesTheAddress()
    {
        Resolve(Bearer(("name", "alice")), "203.0.113.2").Should().Be("ip:203.0.113.2");
    }

    [Fact]
    public void Resolve_NoTokenAndAnAddress_UsesTheAddress()
    {
        Resolve(null, "2001:db8::1").Should().Be("ip:2001:db8::1");
    }

    [Fact]
    public void Resolve_NoTokenAndNoAddress_IsUnresolved()
    {
        Resolve(null, null).Should().Be(RateLimitPartitionKeyResolver.UnresolvedPartitionKey).And.Be("unresolved");
    }

    [Fact]
    public void Resolve_TheSameSubjectOnTwoAddresses_IsOneKey()
    {
        var token = Bearer(("oid", "alice"));

        Resolve(token, "203.0.113.1").Should().Be(Resolve(token, "203.0.113.2"));
    }

    [Fact]
    public void ClientAddressKey_IPv4MappedIPv6_IsTheIPv4Key()
    {
        RateLimitPartitionKeyResolver.ClientAddressKey(IPAddress.Parse("::ffff:192.0.2.9")).Should().Be("ip:192.0.2.9");
    }

    [Fact]
    public void ClientAddressKey_IPv6WithAScopeId_DropsTheScope()
    {
        var scoped = IPAddress.Parse("fe80::1%5");

        RateLimitPartitionKeyResolver.ClientAddressKey(scoped).Should().Be("ip:fe80::1");
    }

    [Fact]
    public void ClientAddressKey_Null_IsNull()
    {
        RateLimitPartitionKeyResolver.ClientAddressKey(null).Should().BeNull();
    }

    // ── Bounded ───────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AValueOfExactlyTheMaximumLength_IsUsedAsIs()
    {
        var value = new string('a', RateLimitPartitionKeyResolver.MaxValueLength);

        Resolve(Bearer(("oid", value)), null).Should().Be("sub:" + value);
    }

    [Fact]
    public void Resolve_AValueOneOverTheMaximum_IsHashed_Bounded_AndStable()
    {
        var value = new string('a', RateLimitPartitionKeyResolver.MaxValueLength + 1);

        var first = Resolve(Bearer(("oid", value)), null);
        var second = Resolve(Bearer(("oid", value)), "203.0.113.9");

        first.Should().MatchRegex("^sub:sha256:[0-9a-f]{64}$");
        first.Length.Should().BeLessThanOrEqualTo(RateLimitPartitionKeyResolver.MaxValueLength);
        second.Should().Be(first);
    }

    [Fact]
    public void Resolve_TwoDifferentLongValues_HaveDifferentKeys()
    {
        var one = new string('a', 5000);
        var other = new string('a', 4999) + "b";

        Resolve(Bearer(("oid", one)), null).Should().NotBe(Resolve(Bearer(("oid", other)), null));
    }

    [Fact]
    public void Resolve_ALongOrganizationValue_IsHashedUnderTheOrgPrefix()
    {
        Resolve(Bearer((OrgClaim, new string('o', 1000))), null).Should().MatchRegex("^org:sha256:[0-9a-f]{64}$");
    }

    // ── Garbage in ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer    ")]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Bearer abc.def.ghi")]
    [InlineData("Bearer ...")]
    [InlineData("Bearer a.b")]
    [InlineData("Bearer a.b.c.d")]
    [InlineData("Bearer a.b.c.d.e")]
    [InlineData("Bearer e30.e30.")]
    [InlineData("Bearer eyJhbGciOiJub25lIn0.bm90LWpzb24.")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abc")]
    [InlineData("bearerabc.def.ghi")]
    public void Resolve_AMalformedOrNonBearerAuthorization_UsesTheAddress_AndNeverThrows(string authorization)
    {
        Resolve(authorization, "203.0.113.5").Should().Be("ip:203.0.113.5");
    }

    [Fact]
    public void Resolve_AVeryLongGarbageToken_UsesTheAddress()
    {
        Resolve("Bearer " + new string('x', 100_000) + ".y.z", "203.0.113.5").Should().Be("ip:203.0.113.5");
    }

    [Fact]
    public void Resolve_ALowerCaseBearerScheme_IsRead()
    {
        Resolve("bearer " + Token(("oid", "alice")), null).Should().Be("sub:alice");
    }
}
