using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <see cref="HttpTenantContext"/> (XD01-379): the caller's own organization always wins; a caller with none (a service
/// principal, a message consumer) resolves to the organization kernel code entered in the unit of work's scope.
/// </summary>
public sealed class HttpTenantContextTests
{
    private static readonly Guid Own = new("a0a0a0a0-0000-4000-8000-000000000379");
    private static readonly Guid Scoped = new("b0b0b0b0-0000-4000-8000-000000000379");
    private static readonly Guid Member = new("c0c0c0c0-0000-4000-8000-000000000471");

    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
    }

    private static CurrentUser User(Guid organization) => new("oid-1", "user@example.com", "User", [], organization);

    private static CallerMembershipOrganization MemberOf(Guid organization)
    {
        var membership = new CallerMembershipOrganization();
        membership.Set(organization);
        return membership;
    }

    private static TenantOrganizationScope Entered(Guid organization)
    {
        var scope = new TenantOrganizationScope();
        scope.Enter(organization);
        return scope;
    }

    [Fact]
    public void CurrentOrganizationId_OwnOrganization_WinsOverTheScope()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Own)), Entered(Scoped));

        context.CurrentOrganizationId.Should().Be(Own);
    }

    [Fact]
    public void CurrentOrganizationId_UserWithoutAnOrganization_TheEmptyGuid_ResolvesToTheScope()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), Entered(Scoped));

        context.CurrentOrganizationId.Should().Be(Scoped);
    }

    [Fact]
    public void CurrentOrganizationId_NoUserAtAll_ResolvesToTheScope()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(null), Entered(Scoped));

        context.CurrentOrganizationId.Should().Be(Scoped);
    }

    [Fact]
    public void CurrentOrganizationId_NeitherAnOrganizationNorAnEnteredScope_IsEmpty()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), new TenantOrganizationScope());

        context.CurrentOrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void CurrentOrganizationId_NoScopeAtAll_IsEmpty_ForAnyUser()
    {
        new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty))).CurrentOrganizationId.Should().Be(Guid.Empty);
        new HttpTenantContext(new FakeCurrentUserAccessor(null), null).CurrentOrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void CurrentOrganizationId_NoScopeButAnOwnOrganization_IsTheOwnOne()
    {
        new HttpTenantContext(new FakeCurrentUserAccessor(User(Own))).CurrentOrganizationId.Should().Be(Own);
    }

    [Fact]
    public void CurrentOrganizationId_ReadsTheScopeLive_SoAnOrganizationEnteredLaterIsSeen()
    {
        var scope = new TenantOrganizationScope();
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(null), scope);
        context.CurrentOrganizationId.Should().Be(Guid.Empty);

        scope.Enter(Scoped);

        context.CurrentOrganizationId.Should().Be(Scoped);
    }

    [Fact]
    public void CurrentOrganizationId_TokenWithoutAnOrganization_IsTheMembershipOne_XD01_471()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), new TenantOrganizationScope(), MemberOf(Member));

        context.CurrentOrganizationId.Should().Be(Member);
    }

    [Fact]
    public void CurrentOrganizationId_TokenOrganization_WinsOverTheMembershipOne_XD01_471()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Own)), null, MemberOf(Member));

        context.CurrentOrganizationId.Should().Be(Own);
    }

    [Fact]
    public void CurrentOrganizationId_MembershipOrganization_WinsOverTheScope_LikeAClaim_XD01_471()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), Entered(Scoped), MemberOf(Member));

        context.CurrentOrganizationId.Should().Be(Member);
    }

    [Fact]
    public void CurrentOrganizationId_EmptyMembership_FallsBackToTheScope_XD01_471()
    {
        var context = new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), Entered(Scoped), new CallerMembershipOrganization());

        context.CurrentOrganizationId.Should().Be(Scoped);
    }

    [Fact]
    public void CallerMembershipOrganization_RefusesTheEmptyGuid_XD01_471()
    {
        var membership = new CallerMembershipOrganization();

        membership.Invoking(m => m.Set(Guid.Empty)).Should().Throw<ArgumentException>();
        membership.OrganizationId.Should().BeNull();
    }
}
