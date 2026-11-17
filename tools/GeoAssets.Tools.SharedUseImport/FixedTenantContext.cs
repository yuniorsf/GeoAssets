using GeoAssets.Core.Interfaces;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>The one organization a run is scoped to: the pole owner named on the command line.</summary>
internal sealed class FixedTenantContext(Guid organizationId) : ITenantContext
{
    public Guid CurrentOrganizationId { get; } = organizationId;
}
