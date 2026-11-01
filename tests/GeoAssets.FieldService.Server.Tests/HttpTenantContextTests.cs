using FluentAssertions;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

public class HttpTenantContextTests
{
    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
    }

    [Fact]
    public void CurrentOrganizationId_AccessorReturnsUserWithOrganizationId_ReturnsThatId()
    {
        var organizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new CurrentUser("oid-1", "user@example.com", "User One", [], organizationId);
        var tenantContext = new HttpTenantContext(new FakeCurrentUserAccessor(user));

        tenantContext.CurrentOrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public void CurrentOrganizationId_AccessorReturnsNull_ReturnsEmptyGuid()
    {
        var tenantContext = new HttpTenantContext(new FakeCurrentUserAccessor(null));

        tenantContext.CurrentOrganizationId.Should().Be(Guid.Empty);
    }
}
