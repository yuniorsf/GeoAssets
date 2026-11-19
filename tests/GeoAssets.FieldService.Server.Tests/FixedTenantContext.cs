using GeoAssets.Core.Interfaces;

namespace GeoAssets.FieldService.Server.Tests;

internal sealed class FixedTenantContext(Guid organizationId) : ITenantContext
{
    public Guid CurrentOrganizationId => organizationId;
}
