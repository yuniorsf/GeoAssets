namespace GeoAssets.Core.Models;

/// <summary>Why <see cref="AssetTypeRoles"/> rejected an asset type's roles.</summary>
public enum AssetTypeRoleDefect
{
    /// <summary>The type carries more than <see cref="AssetTypeRoles.MaxCount"/> roles.</summary>
    TooManyRoles,

    /// <summary>A role is longer than <see cref="AssetTypeRoles.MaxLength"/> characters.</summary>
    RoleTooLong,

    /// <summary>A role is blank or does not match <see cref="AssetTypeRoles.Pattern"/>.</summary>
    MalformedRole,

    /// <summary>The same role appears more than once.</summary>
    DuplicateRole,
}
