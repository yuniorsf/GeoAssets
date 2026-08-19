using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.Identity.Tests.Authentication;

public class ClaimMappingTests
{
    private static ClaimsPrincipal Principal(bool authenticated, params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: authenticated ? "TestAuth" : null);
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void Map_UnauthenticatedPrincipal_ReturnsNull()
    {
        var principal = Principal(authenticated: false, ("oid", "user-1"));

        ClaimMapping.EntraDefault.Map(principal).Should().BeNull();
    }

    [Fact]
    public void Map_NullPrincipal_ReturnsNull()
    {
        ClaimMapping.EntraDefault.Map(null).Should().BeNull();
    }

    [Fact]
    public void Map_EntraDefault_PrimaryOidClaim_ResolvesObjectId()
    {
        var principal = Principal(true, ("oid", "user-1"));

        ClaimMapping.EntraDefault.Map(principal)!.ExternalObjectId.Should().Be("user-1");
    }

    [Fact]
    public void Map_EntraDefault_OidMissing_FallsBackToObjectIdentifierClaim()
    {
        var principal = Principal(true,
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "user-2"));

        ClaimMapping.EntraDefault.Map(principal)!.ExternalObjectId.Should().Be("user-2");
    }

    [Fact]
    public void Map_EntraDefault_NoObjectIdClaimAtAll_ResolvesEmptyString()
    {
        var principal = Principal(true, ("name", "Someone"));

        ClaimMapping.EntraDefault.Map(principal)!.ExternalObjectId.Should().BeEmpty();
    }

    [Fact]
    public void Map_EntraDefault_PreferredUsernameClaim_ResolvesEmail()
    {
        var principal = Principal(true, ("preferred_username", "a@example.com"));

        ClaimMapping.EntraDefault.Map(principal)!.Email.Should().Be("a@example.com");
    }

    [Fact]
    public void Map_EntraDefault_PreferredUsernameMissing_FallsBackToUpn()
    {
        var principal = Principal(true, ("upn", "b@example.com"));

        ClaimMapping.EntraDefault.Map(principal)!.Email.Should().Be("b@example.com");
    }

    [Fact]
    public void Map_EntraDefault_NoVendorEmailClaim_FallsBackToWellKnownClaimTypesEmail()
    {
        // The BCL ClaimTypes.Email fallback applies regardless of vendor mapping.
        var principal = Principal(true, (ClaimTypes.Email, "c@example.com"));

        ClaimMapping.EntraDefault.Map(principal)!.Email.Should().Be("c@example.com");
    }

    [Fact]
    public void Map_EntraDefault_NameClaim_ResolvesDisplayName()
    {
        var principal = Principal(true, ("name", "Ada Lovelace"), ("preferred_username", "ada@example.com"));

        ClaimMapping.EntraDefault.Map(principal)!.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Map_EntraDefault_NoNameClaim_FallsBackToEmailAsDisplayName()
    {
        var principal = Principal(true, ("preferred_username", "ada@example.com"));

        ClaimMapping.EntraDefault.Map(principal)!.DisplayName.Should().Be("ada@example.com");
    }

    [Fact]
    public void Map_EntraDefault_RolesClaim_ResolvesRoles()
    {
        var principal = Principal(true, ("roles", "Administrator"), ("roles", "Supervisor"));

        ClaimMapping.EntraDefault.Map(principal)!.ExternalRoles.Should().BeEquivalentTo(["Administrator", "Supervisor"]);
    }

    [Fact]
    public void Map_EntraDefault_RolesUnionedWithWellKnownClaimTypesRole_Deduplicated()
    {
        var principal = Principal(true, ("roles", "Administrator"), (ClaimTypes.Role, "Administrator"), (ClaimTypes.Role, "Supervisor"));

        ClaimMapping.EntraDefault.Map(principal)!.ExternalRoles.Should().BeEquivalentTo(["Administrator", "Supervisor"]);
    }

    [Fact]
    public void Map_CustomMapping_UsesConfiguredClaimTypesNotEntraDefaults()
    {
        // The whole point of XD01-48: a non-Entra IdP's claim shape must resolve correctly
        // without any code change, purely by supplying a different ClaimMapping.
        var customMapping = new ClaimMapping
        {
            ObjectIdClaimTypes    = ["sub"],
            EmailClaimTypes       = ["mail"],
            DisplayNameClaimTypes = ["given_name"],
            RoleClaimTypes        = ["group"],
        };
        var principal = Principal(true,
            ("sub", "okta-user-1"), ("mail", "user@otherco.com"), ("given_name", "Grace"), ("group", "Engineers"));

        var user = customMapping.Map(principal);

        user!.ExternalObjectId.Should().Be("okta-user-1");
        user.Email.Should().Be("user@otherco.com");
        user.DisplayName.Should().Be("Grace");
        user.ExternalRoles.Should().BeEquivalentTo(["Engineers"]);
    }

    [Fact]
    public void Map_CustomMapping_EntraShapedClaimsDoNotResolve()
    {
        // Non-leakage in the other direction: a custom mapping must not silently also
        // accept the Entra claim types it wasn't configured with.
        var customMapping = new ClaimMapping { ObjectIdClaimTypes = ["sub"] };
        var principal = Principal(true, ("oid", "entra-user-1"));

        customMapping.Map(principal)!.ExternalObjectId.Should().BeEmpty();
    }
}
