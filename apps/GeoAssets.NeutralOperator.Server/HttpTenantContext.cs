using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Resolves <see cref="ITenantContext.CurrentOrganizationId"/> — what the row-level security of this host's databases is
/// stamped with — from the current request's authenticated organization claim, as the kernel's <c>HttpTenantContext</c>
/// does (XD01-200): a caller with an organization always keeps its own; one whose token carries none resolves to the
/// organization this host's code entered in <paramref name="organizationScope"/> for the unit of work (never a header),
/// else <see cref="Guid.Empty"/>.
/// </summary>
internal sealed class HttpTenantContext(ICurrentUserAccessor currentUserAccessor, ITenantOrganizationScope? organizationScope = null) : ITenantContext
{
    public Guid CurrentOrganizationId =>
        currentUserAccessor.GetCurrentUser()?.OrganizationId is { } own && own != Guid.Empty
            ? own
            : organizationScope?.OrganizationId ?? Guid.Empty;
}
