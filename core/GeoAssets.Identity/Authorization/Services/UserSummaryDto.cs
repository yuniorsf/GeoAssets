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
