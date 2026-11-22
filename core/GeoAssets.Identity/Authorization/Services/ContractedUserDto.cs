namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape of <c>GET {prefix}/organizations/{orgId}/contracted-users</c> (XD01-327): an active user of
/// <see cref="OrganizationId"/>, an organization holding an active, unexpired grant from <c>orgId</c>, and therefore
/// eligible for a role on <c>orgId</c>.
/// </summary>
public sealed record ContractedUserDto(
    Guid    Id,
    string  DisplayName,
    string  Email,
    Guid    OrganizationId,
    string? OrganizationName);
