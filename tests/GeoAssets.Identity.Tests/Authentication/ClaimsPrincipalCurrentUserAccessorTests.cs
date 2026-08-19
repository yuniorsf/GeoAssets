using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.Identity.Tests.Authentication;

public class ClaimsPrincipalCurrentUserAccessorTests
{
    private static ClaimsPrincipal AuthenticatedPrincipal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "TestAuth"));

    [Fact]
    public void GetCurrentUser_ResolverReturnsNull_ReturnsNull()
    {
        var accessor = new ClaimsPrincipalCurrentUserAccessor(() => null);

        accessor.GetCurrentUser().Should().BeNull();
    }

    [Fact]
    public void GetCurrentUser_NoMappingSupplied_UsesEntraDefault()
    {
        var principal = AuthenticatedPrincipal(("oid", "user-1"), ("preferred_username", "a@example.com"));
        var accessor = new ClaimsPrincipalCurrentUserAccessor(() => principal);

        var user = accessor.GetCurrentUser();

        user!.ExternalObjectId.Should().Be("user-1");
        user.Email.Should().Be("a@example.com");
    }

    [Fact]
    public void GetCurrentUser_CustomMappingSupplied_IsUsedInsteadOfEntraDefault()
    {
        var customMapping = new ClaimMapping { ObjectIdClaimTypes = ["sub"] };
        var principal = AuthenticatedPrincipal(("sub", "okta-user-1"));
        var accessor = new ClaimsPrincipalCurrentUserAccessor(() => principal, customMapping);

        accessor.GetCurrentUser()!.ExternalObjectId.Should().Be("okta-user-1");
    }
}
