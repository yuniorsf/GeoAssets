namespace GeoAssets.Core.Services;

/// <summary>
/// The one definition of how a cross-organization grant matches a resource (XD01-364, ADR-001 smell H): the workflow's
/// service-order grant rule and the host's organization-resource handler (features, asset types) both use it, so they can
/// never drift apart again. Resource types and actions compare ignoring case; a scope compares exactly.
/// </summary>
public static class OrganizationGrantMatching
{
    /// <summary>A grant with no resource type covers every type of the granting organization's resources.</summary>
    public static bool CoversResourceType(string? grantResourceType, string resourceType) =>
        grantResourceType is null || string.Equals(grantResourceType, resourceType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="allowedActions"/> lists <paramref name="actionCode"/>.</summary>
    public static bool AllowsAction(IEnumerable<string> allowedActions, string actionCode)
    {
        ArgumentNullException.ThrowIfNull(allowedActions);
        return allowedActions.Contains(actionCode, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A grant with no scope covers every scope; a scoped grant covers only a resource of that scope.</summary>
    public static bool CoversScope(string? grantScopeId, string? resourceScopeId) =>
        grantScopeId is null || string.Equals(grantScopeId, resourceScopeId, StringComparison.Ordinal);
}
