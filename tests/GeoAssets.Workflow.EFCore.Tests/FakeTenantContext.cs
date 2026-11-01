using GeoAssets.Core.Interfaces;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>Fixed-tenant <see cref="ITenantContext"/> test double, mirroring GeoAssets.Provider.PostgreSQL.Tests' own.</summary>
internal sealed class FakeTenantContext(Guid organizationId) : ITenantContext
{
    public Guid CurrentOrganizationId { get; } = organizationId;
}
