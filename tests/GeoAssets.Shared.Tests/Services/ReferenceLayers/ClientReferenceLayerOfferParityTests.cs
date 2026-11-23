using FluentAssertions;
using GeoAssets.ReferenceLayerOfferParity;
using GeoAssets.Shared.Services.ReferenceLayers;
using Xunit;
using static GeoAssets.ReferenceLayerOfferParity.ReferenceLayerOfferParityMatrix;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// XD01-331 — the client half of <see cref="ReferenceLayerOfferParityMatrix"/>: <see cref="ReferenceLayerOffer"/>, over the
/// <c>/me</c> answer and the <c>/me/cross-org-entitlements</c> answer the kernel builds for each row, offers COELBA's toggle
/// exactly when the kernel's <c>OrganizationResourceAccess</c> opens COELBA's reference features
/// (<c>GeoAssets.Server.Tests</c>' <c>ServerReferenceLayerOfferParityTests</c>).
/// </summary>
public sealed class ClientReferenceLayerOfferParityTests
{
    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(ReferenceLayerOfferParityMatrix))]
    public void XD01_331_TheMapOffersCoelbaExactlyWhenTheKernelWouldOpenIt(string name)
    {
        var row = Get(name);

        var offered = ReferenceLayerOffer.Organizations(row.Home, row.Entitlements, Now.UtcDateTime);

        offered.Any(o => o.Id == Coelba).Should().Be(row.Offered, row.Name);
    }

    [Fact]
    public void AGrantFromAnotherOrganization_OffersThatOrganization_NotCoelba()
    {
        var row = Get("a grant from another organization does not open COELBA");

        ReferenceLayerOffer.Organizations(row.Home, row.Entitlements, Now.UtcDateTime)
            .Should().Equal(new ReferenceOrganization(Third, "THIRD"));
    }

    [Fact]
    public void TheMatrixAsksForTheOffersPermissionAndResourceType()
    {
        Reference.Should().Be(ReferenceLayerOffer.Permission);
        ReferenceLayerOffer.ResourceType.Should().Be("GeoFeature");
    }
}
