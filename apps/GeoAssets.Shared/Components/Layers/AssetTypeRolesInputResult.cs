using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Components.Layers;

/// <summary>
/// What adding text to the editor's role tags means. On success <see cref="Error"/> is null and
/// <see cref="Roles"/> holds the new tag list; on failure <see cref="Roles"/> is the unchanged
/// list and <see cref="OffendingRole"/> names the role at fault (null for
/// <see cref="AssetTypeRoleDefect.TooManyRoles"/>).
/// </summary>
public sealed record AssetTypeRolesInputResult(
    IReadOnlyList<string> Roles, AssetTypeRoleDefect? Error, string? OffendingRole)
{
    public bool IsValid => Error is null;
}
