using GeoAssets.Core.Interfaces;

namespace GeoAssets.Server;

/// <summary>
/// An <see cref="OrganizationResourceAccess"/> answer (XD01-330): nothing (<see cref="None"/>), every resource
/// (<see cref="All"/>), or only the resources of the scopes in <see cref="ScopeIds"/> (<see cref="IScopeableResource.ScopeId"/>,
/// compared exactly).
/// </summary>
public sealed class OrganizationResourceScope
{
    /// <summary>No access.</summary>
    public static readonly OrganizationResourceScope None = new(granted: false, scopeIds: null);

    /// <summary>Every resource of the organization and type.</summary>
    public static readonly OrganizationResourceScope All = new(granted: true, scopeIds: null);

    private readonly HashSet<string>? _scopeIds;

    private OrganizationResourceScope(bool granted, HashSet<string>? scopeIds)
    {
        IsGranted = granted;
        _scopeIds = scopeIds;
    }

    /// <summary>Whether any resource is reachable.</summary>
    public bool IsGranted { get; }

    /// <summary><c>null</c> for <see cref="None"/> and <see cref="All"/>; otherwise the only scopes reachable (never empty).</summary>
    public IReadOnlyCollection<string>? ScopeIds => _scopeIds;

    /// <summary>Only the resources of these scopes; <see cref="None"/> when there are none.</summary>
    public static OrganizationResourceScope Of(IEnumerable<string> scopeIds)
    {
        var scopes = new HashSet<string>(scopeIds, StringComparer.Ordinal);
        return scopes.Count == 0 ? None : new OrganizationResourceScope(granted: true, scopes);
    }

    /// <summary>
    /// Whether <paramref name="resource"/> is reachable: under <see cref="All"/> any resource; under a set of scopes, only an
    /// <see cref="IScopeableResource"/> of one of them.
    /// </summary>
    public bool Covers(IOrgOwnedResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!IsGranted) return false;
        if (_scopeIds is null) return true;
        return resource is IScopeableResource { ScopeId: { } scopeId } && _scopeIds.Contains(scopeId);
    }
}
