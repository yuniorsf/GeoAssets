using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary><see cref="TenantOrganizationScope"/> (XD01-379): the one organization kernel code enters for a unit of work.</summary>
public sealed class TenantOrganizationScopeTests
{
    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");

    [Fact]
    public void OrganizationId_BeforeAnyEnter_IsNull()
    {
        var scope = new TenantOrganizationScope();

        scope.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void Enter_NonEmptyOrganization_IsReadBack()
    {
        var scope = new TenantOrganizationScope();

        scope.Enter(OrgX);

        scope.OrganizationId.Should().Be(OrgX);
    }

    [Fact]
    public void Enter_EmptyOrganization_ThrowsArgumentException_AndEntersNothing()
    {
        var scope = new TenantOrganizationScope();

        var act = () => scope.Enter(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("organizationId");
        scope.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void Enter_TheSameOrganizationTwice_IsAllowed()
    {
        var scope = new TenantOrganizationScope();
        scope.Enter(OrgX);

        var act = () => scope.Enter(OrgX);

        act.Should().NotThrow();
        scope.OrganizationId.Should().Be(OrgX);
    }

    [Fact]
    public void Enter_ADifferentOrganizationAfterOne_ThrowsInvalidOperation_AndKeepsTheFirst()
    {
        var scope = new TenantOrganizationScope();
        scope.Enter(OrgX);

        var act = () => scope.Enter(OrgY);

        act.Should().Throw<InvalidOperationException>();
        scope.OrganizationId.Should().Be(OrgX);
    }

    [Fact]
    public void EmptyOrganization_AfterAnEnteredOne_StillThrowsArgumentException()
    {
        var scope = new TenantOrganizationScope();
        scope.Enter(OrgX);

        var act = () => scope.Enter(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TwoScopes_AreIndependent()
    {
        var first = new TenantOrganizationScope();
        var second = new TenantOrganizationScope();

        first.Enter(OrgX);
        second.Enter(OrgY);

        (first.OrganizationId, second.OrganizationId).Should().Be((OrgX, OrgY));
    }
}
