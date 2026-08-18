using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Resource-based authorization requirement for an <c>IOrgOwnedResource</c> (XD01-21):
/// the caller must hold <see cref="PermissionCode"/> AND either belong to the resource's
/// owning organization or hold a matching active <c>OrganizationGrant</c>. See
/// <see cref="OrgResourceAuthorizationHandler"/> for the full evaluation.
/// </summary>
public sealed class OrgResourceRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
