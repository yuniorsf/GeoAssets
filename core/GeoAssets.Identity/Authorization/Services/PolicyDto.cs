using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for <c>GET /api/identity/policies</c> — see <see cref="AuthorizationContextDto"/> for why.</summary>
public sealed record PolicyDto(
    Guid                                Id,
    string                              Name,
    string                              Description,
    PolicyOperator                      Operator,
    IReadOnlyList<PolicyRequirementDto> Requirements);
