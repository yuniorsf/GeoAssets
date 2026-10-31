using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Services;

/// <summary>
/// No-op <see cref="ITenantContext"/> resolving to <see cref="Guid.Empty"/> — the default for
/// hosts that have not wired real per-request tenant resolution (single-tenant deployments,
/// background jobs, design-time tooling). Register this when you want tenant-scoped
/// infrastructure to behave exactly as it did before tenant scoping existed.
///
/// <code>
///   services.TryAddSingleton&lt;ITenantContext, NullTenantContext&gt;();
/// </code>
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    public Guid CurrentOrganizationId => Guid.Empty;

    private NullTenantContext()
    {
    }
}
