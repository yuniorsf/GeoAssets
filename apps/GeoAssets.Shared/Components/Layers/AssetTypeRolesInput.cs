using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Components.Layers;

/// <summary>
/// The pure decisions behind <c>AssetTypeManager</c>'s role tags (XD01-367): adding typed text
/// (one role, or several separated by commas or spaces) to the current tags, removing one, and
/// choosing the localization key and arguments for an error. The rules themselves are
/// <see cref="AssetTypeRoles"/>'s.
/// </summary>
public static class AssetTypeRolesInput
{
    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Adds the roles in <paramref name="text"/> to <paramref name="current"/>. Blank text adds
    /// nothing. The whole addition is rejected when the resulting tags would break a rule.
    /// </summary>
    public static AssetTypeRolesInputResult Add(IReadOnlyList<string> current, string? text)
    {
        ArgumentNullException.ThrowIfNull(current);

        var added = (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (added.Length == 0) return new AssetTypeRolesInputResult(current, null, null);

        List<string> roles = [.. current, .. added];
        return AssetTypeRoles.FindDefect(roles, out var offendingRole) is { } defect
            ? new AssetTypeRolesInputResult(current, defect, offendingRole)
            : new AssetTypeRolesInputResult(roles, null, null);
    }

    /// <summary>The tags without <paramref name="role"/> (ordinal).</summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> current, string role)
    {
        ArgumentNullException.ThrowIfNull(current);
        return [.. current.Where(r => !string.Equals(r, role, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Localization key of the message for <paramref name="error"/>. See
    /// <see cref="ErrorArguments"/> for the arguments each message takes.
    /// </summary>
    public static string ErrorKey(AssetTypeRoleDefect error) => error switch
    {
        AssetTypeRoleDefect.TooManyRoles => "map.layers.roles.error.tooMany",
        AssetTypeRoleDefect.RoleTooLong => "map.layers.roles.error.tooLong",
        AssetTypeRoleDefect.DuplicateRole => "map.layers.roles.error.duplicate",
        _ => "map.layers.roles.error.malformed",
    };

    /// <summary>
    /// The format arguments <see cref="ErrorKey"/>'s message expects, in order: the maximum count
    /// for too many roles, the role and the maximum length for a role too long, the role otherwise.
    /// </summary>
    public static object[] ErrorArguments(AssetTypeRoleDefect error, string? offendingRole) => error switch
    {
        AssetTypeRoleDefect.TooManyRoles => [AssetTypeRoles.MaxCount],
        AssetTypeRoleDefect.RoleTooLong => [offendingRole ?? string.Empty, AssetTypeRoles.MaxLength],
        _ => [offendingRole ?? string.Empty],
    };
}
