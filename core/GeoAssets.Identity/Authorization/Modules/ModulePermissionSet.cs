namespace GeoAssets.Identity.Authorization.Modules;

/// <summary>A module's permissions and role bundles, as one upsert (XD01-377) — what an out-of-process vertical PUTs.</summary>
public sealed record ModulePermissionSet
{
    public IReadOnlyList<PermissionDefinition> Permissions { get; init => field = value ?? []; } = [];
    public IReadOnlyList<RoleBundleContribution> RoleBundles { get; init => field = value ?? []; } = [];

    /// <summary>The first reason the set cannot be applied, or <c>null</c> when it can.</summary>
    public string? FindProblem()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in Permissions)
        {
            if (permission is null) return "A permission is missing.";
            if (string.IsNullOrWhiteSpace(permission.Code) || string.IsNullOrWhiteSpace(permission.Resource)
                || string.IsNullOrWhiteSpace(permission.Action) || string.IsNullOrWhiteSpace(permission.DescriptionKey))
                return "A permission needs a code, a resource, an action and a description key.";
            if (!codes.Add(permission.Code)) return $"Permission '{permission.Code}' is defined twice.";
        }

        foreach (var bundle in RoleBundles)
        {
            if (bundle is null) return "A role bundle is missing.";
            if (bundle.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(bundle.RoleName))
                return "A role bundle needs a role id and a role name.";
            if (bundle.PermissionCodes is null || bundle.PermissionCodes.Any(string.IsNullOrWhiteSpace))
                return $"Role bundle '{bundle.RoleName}' has a blank permission code.";
        }

        return null;
    }
}
