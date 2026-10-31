using GeoAssets.Core.Interfaces;

namespace GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;

/// <summary>Settable <see cref="ITenantContext"/> test double for exercising tenant row isolation.</summary>
internal sealed class FakeTenantContext(Guid organizationId) : ITenantContext
{
    public Guid CurrentOrganizationId { get; } = organizationId;
}
