namespace GeoAssets.Core.Models;

/// <summary>
/// Thrown when an <see cref="AssetType"/>'s roles break <see cref="AssetTypeRoles"/>'s rules.
/// <see cref="Defect"/> carries the reason as a stable, typed value; <see cref="Role"/> the role
/// at fault, when there is one.
/// </summary>
public sealed class InvalidAssetTypeRolesException : InvalidOperationException
{
    public AssetTypeRoleDefect Defect { get; }
    public string? Role { get; }

    public InvalidAssetTypeRolesException(AssetTypeRoleDefect defect, string? role)
        : base(role is null ? $"The asset type's roles are invalid: {defect}." : $"The asset type role '{role}' is invalid: {defect}.")
    {
        Defect = defect;
        Role = role;
    }

    public InvalidAssetTypeRolesException()
    {
    }

    public InvalidAssetTypeRolesException(string message) : base(message)
    {
    }

    public InvalidAssetTypeRolesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
