using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// <see cref="OrganizationGrantMatching"/> (XD01-364): the one definition of how a cross-organization grant matches a
/// resource, shared by the workflow's grant rule and the host's organization-resource handler.
/// </summary>
public sealed class OrganizationGrantMatchingTests
{
    // ── Resource type ─────────────────────────────────────────────────────────

    [Fact]
    public void CoversResourceType_NullGrantType_CoversEveryType() =>
        OrganizationGrantMatching.CoversResourceType(null, "ServiceOrder").Should().BeTrue();

    [Fact]
    public void CoversResourceType_SameTypeDifferingInCase_Covers() =>
        OrganizationGrantMatching.CoversResourceType("serviceorder", "ServiceOrder").Should().BeTrue();

    [Fact]
    public void CoversResourceType_DifferentType_DoesNotCover() =>
        OrganizationGrantMatching.CoversResourceType("GeoFeature", "ServiceOrder").Should().BeFalse();

    // ── Actions ───────────────────────────────────────────────────────────────

    [Fact]
    public void AllowsAction_ActionDifferingInCase_Allows() =>
        OrganizationGrantMatching.AllowsAction(["ServiceOrders:VIEW"], "serviceorders:view").Should().BeTrue();

    [Fact]
    public void AllowsAction_ActionNotListed_DoesNotAllow() =>
        OrganizationGrantMatching.AllowsAction(["serviceorders:view"], "serviceorders:cancel").Should().BeFalse();

    [Fact]
    public void AllowsAction_NoActions_DoesNotAllow() =>
        OrganizationGrantMatching.AllowsAction([], "serviceorders:view").Should().BeFalse();

    [Fact]
    public void AllowsAction_NullActions_Throws()
    {
        var act = () => OrganizationGrantMatching.AllowsAction(null!, "serviceorders:view");

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Scope ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "municipio-1")]
    public void CoversScope_NullGrantScope_CoversEveryScope(string? grantScope, string? resourceScope) =>
        OrganizationGrantMatching.CoversScope(grantScope, resourceScope).Should().BeTrue();

    [Fact]
    public void CoversScope_ExactScope_Covers() =>
        OrganizationGrantMatching.CoversScope("municipio-1", "municipio-1").Should().BeTrue();

    [Fact]
    public void CoversScope_ScopeDifferingInCase_DoesNotCover() =>
        OrganizationGrantMatching.CoversScope("municipio-1", "MUNICIPIO-1").Should().BeFalse();

    [Fact]
    public void CoversScope_ScopedGrantAgainstAResourceWithNoScope_DoesNotCover() =>
        OrganizationGrantMatching.CoversScope("municipio-1", null).Should().BeFalse();
}
