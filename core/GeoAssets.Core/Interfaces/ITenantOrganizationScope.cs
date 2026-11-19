namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Scopes the current unit of work (one DI scope: an HTTP request, one consumed message) to an organization when the
/// caller itself carries none — a service principal acting for a vertical on a machine-to-machine route, or a message
/// consumer — so <see cref="ITenantContext.CurrentOrganizationId"/>, and with it the database row-level security
/// session, follow the organization the operation is for (XD01-379).
///
/// Never widens a caller that has an organization: a host's <see cref="ITenantContext"/> keeps reporting the caller's
/// own organization and ignores the scope. Within one DI scope the organization can be entered once; entering a
/// different one throws, so a single unit of work never mixes organizations.
/// </summary>
public interface ITenantOrganizationScope
{
    /// <summary>The organization entered in this scope, or null when none was.</summary>
    Guid? OrganizationId { get; }

    /// <summary>Scopes the rest of this unit of work to <paramref name="organizationId"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="organizationId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="InvalidOperationException">A different organization was already entered in this scope.</exception>
    void Enter(Guid organizationId);
}
