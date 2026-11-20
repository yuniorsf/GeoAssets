using GeoAssets.Core.Interfaces;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

internal sealed class FixedTenantContext(Guid organizationId) : ITenantContext
{
    public Guid CurrentOrganizationId { get; } = organizationId;
}
