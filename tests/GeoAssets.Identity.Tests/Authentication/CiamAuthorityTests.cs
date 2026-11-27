using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.Identity.Tests.Authentication;

/// <summary>XD01-452: the CIAM authority a service caller asks its client-credentials token from.</summary>
public sealed class CiamAuthorityTests
{
    private const string TenantId = "94bb6627-6a6f-4219-b6d2-ce9ca5e82215";

    [Theory]
    [InlineData("https://contoso.ciamlogin.com/")]
    [InlineData("https://contoso.ciamlogin.com")]
    [InlineData("  https://contoso.ciamlogin.com/ignored/path?q=1  ")]
    public void TheAuthority_IsTheInstanceHostFollowedByTheTenantId(string instance)
    {
        CiamAuthority.TryCreate(instance, TenantId, out var authority).Should().BeTrue();

        authority!.AbsoluteUri.Should().Be($"https://contoso.ciamlogin.com/{TenantId}");
    }

    [Fact]
    public void AnInstancePort_IsKept()
    {
        CiamAuthority.TryCreate("https://localhost:8443/", TenantId, out var authority).Should().BeTrue();

        authority!.AbsoluteUri.Should().Be($"https://localhost:8443/{TenantId}");
    }

    [Fact]
    public void TheTenantId_IsTrimmedAndEscaped()
    {
        CiamAuthority.TryCreate("https://contoso.ciamlogin.com/", " a b ", out var authority).Should().BeTrue();

        authority!.AbsolutePath.Should().Be("/a%20b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("contoso.ciamlogin.com")]
    [InlineData("/relative/")]
    [InlineData("http://contoso.ciamlogin.com/")]
    [InlineData("ftp://contoso.ciamlogin.com/")]
    public void AMissingRelativeOrNonHttpsInstance_IsRefused(string? instance)
    {
        CiamAuthority.TryCreate(instance, TenantId, out var authority).Should().BeFalse();

        authority.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankTenantId_IsRefused(string? tenantId)
    {
        CiamAuthority.TryCreate("https://contoso.ciamlogin.com/", tenantId, out var authority).Should().BeFalse();

        authority.Should().BeNull();
    }
}
