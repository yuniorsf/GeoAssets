using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// <see cref="HttpTenantContext"/> (XD01-382): what this host's row-level security is stamped with is the caller's organization
/// claim, else the organization host code entered for the unit of work — never anything a request can state.
/// </summary>
public sealed class HttpTenantContextTests
{
    private static readonly Guid Own = new("a0a0a0a0-0000-4000-8000-000000000382");
    private static readonly Guid Scoped = new("b0b0b0b0-0000-4000-8000-000000000382");

    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
    }

    private static CurrentUser User(Guid organization) => new("oid-1", "user@example.com", "User", [], organization);

    private static TenantOrganizationScope Entered(Guid organization)
    {
        var scope = new TenantOrganizationScope();
        scope.Enter(organization);
        return scope;
    }

    [Fact]
    public void CurrentOrganizationId_TheCallersOwnOrganization_WinsOverTheScope() =>
        new HttpTenantContext(new FakeCurrentUserAccessor(User(Own)), Entered(Scoped)).CurrentOrganizationId.Should().Be(Own);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentOrganizationId_ACallerWithoutAnOrganization_ResolvesToTheEnteredScope(bool noUserAtAll) =>
        new HttpTenantContext(new FakeCurrentUserAccessor(noUserAtAll ? null : User(Guid.Empty)), Entered(Scoped)).CurrentOrganizationId.Should().Be(Scoped);

    [Fact]
    public void CurrentOrganizationId_NeitherAnOrganizationNorAScope_IsEmpty() =>
        new HttpTenantContext(new FakeCurrentUserAccessor(User(Guid.Empty)), new TenantOrganizationScope()).CurrentOrganizationId.Should().Be(Guid.Empty);

    [Fact]
    public void CurrentOrganizationId_WithNoScopeService_IsEmptyForACallerWithoutAnOrganization() =>
        new HttpTenantContext(new FakeCurrentUserAccessor(null)).CurrentOrganizationId.Should().Be(Guid.Empty);
}
