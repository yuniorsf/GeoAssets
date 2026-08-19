using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape for <c>GET /api/identity/me</c> — a flattened projection of
/// <see cref="AuthorizationContext"/>. Deliberately not the raw <see cref="AppUser"/>/
/// <see cref="AuthorizationContext"/> types: their EF navigation properties
/// (<c>AppUser.UserRoles[].Role.UserRoles</c>, etc.) round-trip back to the owning
/// entity once EF's relationship fixup populates them, which <c>System.Text.Json</c>
/// rejects as a reference cycle. Shared by <c>GeoAssets.Server</c> (produces) and
/// <c>GeoAssets.Web</c> (consumes) so the shape can't drift between them.
/// </summary>
public sealed record AuthorizationContextDto(
    Guid                     Id,
    string                   Email,
    string                   DisplayName,
    Guid?                    OrganizationId,
    IReadOnlyList<string>    Roles,
    IReadOnlyList<ClaimDto>  Claims,
    IReadOnlyList<string>    Permissions);

public sealed record ClaimDto(string Type, string Value);

/// <summary>Wire shape for <c>GET /api/identity/policies</c> — see <see cref="AuthorizationContextDto"/> for why.</summary>
public sealed record PolicyDto(
    Guid                                Id,
    string                              Name,
    string                              Description,
    PolicyOperator                      Operator,
    IReadOnlyList<PolicyRequirementDto> Requirements);

public sealed record PolicyRequirementDto(RequirementType Type, string Value, string? ClaimValue);
