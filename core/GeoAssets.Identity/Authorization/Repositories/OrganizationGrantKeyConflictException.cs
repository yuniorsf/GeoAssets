using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Thrown by <see cref="IOrganizationGrantRepository.SaveChangesAsync"/> when the store refused the save because another
/// active grant already holds the same <see cref="OrganizationGrantKey"/> — the one-active-grant-per-key rule (XD01-394),
/// typically a concurrent upsert that stored its grant first. Before throwing, the repository discards the grant writes
/// (and their grant-sync outbox entries) of the failed save, so a later save cannot resubmit them and a re-read sees the
/// store's state.
/// </summary>
public sealed class OrganizationGrantKeyConflictException : Exception
{
    public OrganizationGrantKeyConflictException()
        : this("Another active organization grant already has this key.")
    {
    }

    public OrganizationGrantKeyConflictException(string message)
        : base(message)
    {
    }

    public OrganizationGrantKeyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OrganizationGrantKeyConflictException(OrganizationGrantKey? key, Exception innerException)
        : base(Describe(key), innerException)
    {
        Key = key;
    }

    /// <summary>The key the refused write named, when the repository could tell which grant it was; otherwise null.</summary>
    public OrganizationGrantKey? Key { get; }

    private static string Describe(OrganizationGrantKey? key) =>
        key is null
            ? "Another active organization grant already has this key."
            : $"Another active organization grant already has the key (grantee {key.GranteeOrganizationId}, resource "
              + $"{key.ResourceOrganizationId}, resourceType {key.ResourceType ?? "null"}, scopeId {key.ScopeId ?? "null"}).";
}
