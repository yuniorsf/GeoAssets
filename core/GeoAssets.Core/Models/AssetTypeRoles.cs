using System.Text.RegularExpressions;

namespace GeoAssets.Core.Models;

/// <summary>
/// The rules of an <see cref="AssetType"/>'s semantic roles (<see cref="AssetType.Roles"/>,
/// XD01-367): at most <see cref="MaxCount"/> distinct roles, each at most
/// <see cref="MaxLength"/> characters of lower-kebab text with an optional lower-kebab
/// namespace prefix (<see cref="Pattern"/>, e.g. <c>occupiable-support</c>, <c>isp:pole</c>).
/// The single definition shared by the editor, <c>ValidatingAssetProvider</c> and the server's
/// asset-type endpoint.
/// </summary>
public static class AssetTypeRoles
{
    /// <summary>Most roles one asset type may carry.</summary>
    public const int MaxCount = 16;

    /// <summary>Longest role, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>A lower-kebab tag, optionally prefixed by a lower-kebab namespace and a colon.</summary>
    public const string Pattern = @"^[a-z0-9]+(?:-[a-z0-9]+)*(?::[a-z0-9]+(?:-[a-z0-9]+)*)?\z";

    private static readonly Regex RoleRegex = new(Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    /// <summary>Whether <paramref name="role"/> is a single well-formed role (length and pattern).</summary>
    public static bool IsWellFormed(string? role) =>
        !string.IsNullOrEmpty(role) && role.Length <= MaxLength && RoleRegex.IsMatch(role);

    /// <summary>Returns the first rule <paramref name="roles"/> break, or <c>null</c> when they are valid.
    /// <c>null</c> roles are valid (no roles).</summary>
    public static AssetTypeRoleDefect? FindDefect(IReadOnlyCollection<string>? roles) => FindDefect(roles, out _);

    /// <summary>
    /// Returns the first rule <paramref name="roles"/> break, or <c>null</c> when they are valid;
    /// <paramref name="offendingRole"/> names the role at fault (<c>null</c> for
    /// <see cref="AssetTypeRoleDefect.TooManyRoles"/> or when valid).
    /// </summary>
    public static AssetTypeRoleDefect? FindDefect(IReadOnlyCollection<string>? roles, out string? offendingRole)
    {
        offendingRole = null;
        if (roles is null || roles.Count == 0) return null;
        if (roles.Count > MaxCount) return AssetTypeRoleDefect.TooManyRoles;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            offendingRole = role;
            if (role is { Length: > MaxLength }) return AssetTypeRoleDefect.RoleTooLong;
            if (!IsWellFormed(role)) return AssetTypeRoleDefect.MalformedRole;
            if (!seen.Add(role)) return AssetTypeRoleDefect.DuplicateRole;
        }

        offendingRole = null;
        return null;
    }

    /// <summary>Throws <see cref="InvalidAssetTypeRolesException"/> when <paramref name="roles"/> are invalid.</summary>
    public static void EnsureValid(IReadOnlyCollection<string>? roles)
    {
        if (FindDefect(roles, out var offendingRole) is { } defect)
            throw new InvalidAssetTypeRolesException(defect, offendingRole);
    }

    /// <summary>Whether <paramref name="assetType"/> carries <paramref name="role"/> (ordinal).</summary>
    public static bool HasRole(AssetType assetType, string role)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(role);
        return assetType.Roles.Contains(role, StringComparer.Ordinal);
    }
}
