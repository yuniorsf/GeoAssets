using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Requires the caller's AppUser to already have an organization assigned
/// (AppUser.OrganizationId non-null). See <see cref="RequireOrganizationAuthorizationHandler"/>
/// for why this is deliberately NOT enforced at JWT token-validation time (XD01-49).
/// </summary>
public sealed class RequireOrganizationRequirement : IAuthorizationRequirement;
