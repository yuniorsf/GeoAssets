using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services.ReferenceLayers;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// XD01-331 — <see cref="ReferenceLayerOffer"/> beyond the parity matrix: what it offers when several organizations are
/// entitled, how they are named and ordered, and the client-only guards (own organization, expiry against the client's clock).
/// </summary>
public sealed class ReferenceLayerOfferTests
{
    private const string Reference = ReferenceLayerOffer.Permission;

    private static readonly Guid Own = new("b1b00000-0000-4000-8000-000000000331");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000331");
    private static readonly Guid Embasa = new("e0ba5a00-0000-4000-8000-000000000331");
    private static readonly Guid Unnamed = new("00000000-0000-4000-8000-0000000003a1");
    private static readonly DateTime Now = new(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

    private static AuthorizationContext Home(params string[] permissions) => new()
    {
        User = new AppUser { Id = Guid.NewGuid(), Email = "u@bimbo.example", DisplayName = "U", OrganizationId = Own, CreatedAt = Now },
        Roles = [],
        Claims = [],
        Permissions = permissions,
    };

    private static CrossOrgGrantEntitlementDto Grant(Guid organization, string? name, DateTime? expiresAt = null, string? resourceType = "GeoFeature") =>
        new(organization, name, resourceType, null, [], [Reference], null, RequiresScopedRole: false, expiresAt);

    private static CrossOrgEntitlementsDto Entitlements(params CrossOrgGrantEntitlementDto[] grants) => new(grants, []);

    [Fact]
    public void SeveralEntitledOrganizations_AreOfferedOnceEach_ByName()
    {
        var offered = ReferenceLayerOffer.Organizations(
            Home(Reference),
            Entitlements(Grant(Embasa, "EMBASA"), Grant(Coelba, "COELBA"), Grant(Coelba, "COELBA", resourceType: null)),
            Now);

        offered.Should().Equal(new ReferenceOrganization(Coelba, "COELBA"), new ReferenceOrganization(Embasa, "EMBASA"));
    }

    [Fact]
    public void AnOrganizationTheEntitlementsDoNotName_IsShownByItsId()
    {
        var offered = ReferenceLayerOffer.Organizations(Home(Reference), Entitlements(Grant(Unnamed, null)), Now);

        offered.Should().ContainSingle().Which.Name.Should().Be(Unnamed.ToString());
    }

    [Fact]
    public void TheCallersOwnOrganization_AndTheEmptyOrganization_AreNeverOffered()
    {
        var offered = ReferenceLayerOffer.Organizations(
            Home(Reference), Entitlements(Grant(Own, "BIMBO"), Grant(Guid.Empty, "nobody")), Now);

        offered.Should().BeEmpty("the user's own assets are on the map already, through the provider");
    }

    [Fact]
    public void AGrantThatExpiredSinceTheEntitlementsWereFetched_IsNotOffered()
    {
        var offered = ReferenceLayerOffer.Organizations(
            Home(Reference), Entitlements(Grant(Coelba, "COELBA", expiresAt: Now), Grant(Embasa, "EMBASA", expiresAt: Now.AddSeconds(1))), Now);

        offered.Should().ContainSingle().Which.Id.Should().Be(Embasa);
    }

    [Fact]
    public void AnyCandidate_IsTrueOnlyForAGrantThatCouldOpenTheLayer()
    {
        ReferenceLayerOffer.AnyCandidate(CrossOrgEntitlementsDto.None).Should().BeFalse();
        ReferenceLayerOffer.AnyCandidate(Entitlements(Grant(Coelba, "COELBA", resourceType: "Feature"))).Should().BeFalse();
        ReferenceLayerOffer.AnyCandidate(Entitlements(
            new CrossOrgGrantEntitlementDto(Coelba, "COELBA", "GeoFeature", null, ["cut"], [Reference], null, false, null))).Should().BeFalse();
        ReferenceLayerOffer.AnyCandidate(Entitlements(Grant(Coelba, "COELBA"))).Should().BeTrue();
    }

    [Fact]
    public void NullLists_FromAnOlderServer_OfferNothing_InsteadOfThrowing()
    {
        var entitlements = new CrossOrgEntitlementsDto(null!, null!);

        ReferenceLayerOffer.AnyCandidate(entitlements).Should().BeFalse();
        ReferenceLayerOffer.Organizations(Home(Reference), entitlements, Now).Should().BeEmpty();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        FluentActions.Invoking(() => ReferenceLayerOffer.AnyCandidate(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ReferenceLayerOffer.Organizations(null!, CrossOrgEntitlementsDto.None, Now)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ReferenceLayerOffer.Organizations(Home(), null!, Now)).Should().Throw<ArgumentNullException>();
    }
}
