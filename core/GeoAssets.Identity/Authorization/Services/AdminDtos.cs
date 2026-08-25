using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shapes for the identity admin endpoints (XD01-54 Phase 1). Deliberately not the raw
/// <c>AppUser</c>/<c>AppRole</c>/<c>AppPermission</c> types — see <see cref="AuthorizationContextDto"/>
/// for why: their EF navigation properties round-trip back to the owning entity once EF's
/// relationship fixup populates them, which <c>System.Text.Json</c> rejects as a reference cycle.
/// Shared by <c>GeoAssets.Server</c> (produces) and <c>GeoAssets.Web</c> (consumes) so the shape
/// can't drift between them.
/// </summary>
public sealed record UserSummaryDto(
    Guid   Id,
    string Email,
    string DisplayName,
    bool   IsActive,
    Guid?  OrganizationId);

public sealed record UserDetailDto(
    Guid                  Id,
    string                Email,
    string                DisplayName,
    bool                  IsActive,
    Guid?                 OrganizationId,
    DateTime              CreatedAt,
    DateTime?             LastLoginAt,
    IReadOnlyList<Guid>   RoleIds);

/// <summary>
/// Admin write payload for a user. <c>Email</c>/<c>ExternalObjectId</c> are deliberately absent —
/// users are JIT-provisioned from the IdP on first login (see <c>AppUser</c>), so there is no
/// "create user" operation and identity fields stay immutable via this admin surface.
/// </summary>
public sealed record UserUpdateDto(
    string DisplayName,
    bool   IsActive,
    Guid?  OrganizationId);

public sealed record RoleSummaryDto(
    Guid   Id,
    string Name,
    string Description,
    bool   IsBuiltIn);

public sealed record RoleDetailDto(
    Guid                Id,
    string              Name,
    string              Description,
    bool                IsBuiltIn,
    IReadOnlyList<Guid> PermissionIds);

/// <summary>
/// Admin write payload for a role. <c>IsBuiltIn</c> is deliberately absent — it's
/// server-controlled (always <see langword="false"/> for admin-created roles).
/// </summary>
public sealed record RoleWriteDto(
    string Name,
    string Description);

public sealed record PermissionDto(
    Guid   Id,
    string Code,
    string Resource,
    string Action,
    string Description);

/// <summary>Wire shape for <c>GET /api/identity/rolesync/status</c> (XD01-63).</summary>
public sealed record RoleSyncStatusDto(bool Enabled);

/// <summary>Wire shape for <c>GET /api/identity/invitations/status</c> (XD01-69).</summary>
public sealed record InvitationStatusDto(bool Enabled);

/// <summary>Wire shape for a <c>PendingInvitation</c> row (XD01-69).</summary>
public sealed record PendingInvitationDto(
    Guid             Id,
    string           Email,
    string           ExternalObjectId,
    Guid             InvitedByUserId,
    DateTime         InvitedAt,
    DateTime?        RedeemedAt,
    InvitationStatus Status);

/// <summary>Admin write payload to create an invitation.</summary>
public sealed record InvitationCreateDto(string Email, string DisplayName);
