using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Resolved authorization state for the current user.
/// Loaded once per request/evaluation and passed to policy evaluations
/// to avoid multiple round-trips to the database.
/// </summary>
public sealed class AuthorizationContext
{
    public required AppUser                   User        { get; init; }
    public required IReadOnlyList<string>     Roles       { get; init; }
    public required IReadOnlyList<UserClaim>  Claims      { get; init; }
    public required IReadOnlyList<string>     Permissions { get; init; }

    /// <summary>Returns true if the user has the specified role (case-insensitive).</summary>
    public bool HasRole(string roleName)
        => Roles.Any(r => r.Equals(roleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the user has the specified claim type (optionally matching a value).</summary>
    public bool HasClaim(string claimType, string? claimValue = null)
        => Claims.Any(c =>
            c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase) &&
            (claimValue is null || c.Value.Equals(claimValue, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Returns true if the user has the specified permission code (case-insensitive).</summary>
    public bool HasPermission(string permissionCode)
        => Permissions.Any(p => p.Equals(permissionCode, StringComparison.OrdinalIgnoreCase));
}
