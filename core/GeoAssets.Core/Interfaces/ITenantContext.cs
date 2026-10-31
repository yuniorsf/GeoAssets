namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Resolves the organization that scopes the current unit of work (an HTTP request, a
/// background job iteration, etc.) for multi-tenant row isolation.
///
/// <see cref="Guid.Empty"/> is the "no organization assigned" sentinel, matching
/// <see cref="IOrgOwnedResource"/>'s own convention — a context resolving to
/// <see cref="Guid.Empty"/> scopes to exactly the rows that also default to
/// <see cref="Guid.Empty"/> (today's single-tenant deployments), not to "all organizations".
/// </summary>
public interface ITenantContext
{
    Guid CurrentOrganizationId { get; }
}
