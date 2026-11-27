namespace GeoAssets.Server;

/// <summary>
/// The signed-in caller's organization as the identity database records it (<c>AppUser.OrganizationId</c>, the
/// organization an administrator assigned), for a caller whose token carries no organization claim (XD01-471). Set once
/// per request by <see cref="CallerMembershipOrganizationExtensions.UseCallerMembershipOrganization"/> when
/// <c>GeoAssets:Tenancy:OrganizationFromMembership</c> is on; <see cref="HttpTenantContext"/> reads it exactly like a
/// token's organization claim. Empty otherwise.
/// </summary>
public sealed class CallerMembershipOrganization
{
    /// <summary>The caller's organization from the identity database, or null when none was resolved.</summary>
    public Guid? OrganizationId { get; private set; }

    /// <summary>Records the caller's organization for the rest of the request.</summary>
    /// <exception cref="ArgumentException"><paramref name="organizationId"/> is <see cref="Guid.Empty"/>.</exception>
    public void Set(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("A caller's organization needs a non-empty organization id.", nameof(organizationId));

        OrganizationId = organizationId;
    }
}
