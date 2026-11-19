using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Services;

/// <summary>Scoped <see cref="ITenantOrganizationScope"/>: holds the one organization entered in the current DI scope.</summary>
public sealed class TenantOrganizationScope : ITenantOrganizationScope
{
    public Guid? OrganizationId { get; private set; }

    public void Enter(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("An organization scope needs a non-empty organization id.", nameof(organizationId));

        if (OrganizationId is { } entered && entered != organizationId)
            throw new InvalidOperationException("This unit of work is already scoped to another organization.");

        OrganizationId = organizationId;
    }
}
