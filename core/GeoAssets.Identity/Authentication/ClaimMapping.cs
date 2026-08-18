using System.Security.Claims;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Configurable IdP claim-type mapping (XD01-48) so <see cref="ICurrentUserAccessor"/>
/// implementations don't hardcode one vendor's claim shape. Each list below is checked in
/// order before falling back to the corresponding well-known <see cref="ClaimTypes"/> URI,
/// which every OIDC/WS-Fed middleware maps *some* claim onto regardless of vendor.
/// </summary>
public sealed class ClaimMapping
{
    public IReadOnlyList<string> ObjectIdClaimTypes     { get; init; } = [];
    public IReadOnlyList<string> EmailClaimTypes        { get; init; } = [];
    public IReadOnlyList<string> DisplayNameClaimTypes  { get; init; } = [];
    public IReadOnlyList<string> RoleClaimTypes         { get; init; } = [];

    /// <summary>
    /// The claim shape Microsoft Entra ID / Entra External ID (CIAM) tokens use — this
    /// codebase's only IdP today, preserved as the default so existing deployments need no
    /// configuration change.
    /// </summary>
    public static readonly ClaimMapping EntraDefault = new()
    {
        ObjectIdClaimTypes    = ["oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"],
        EmailClaimTypes       = ["preferred_username", "upn"],
        DisplayNameClaimTypes = ["name"],
        RoleClaimTypes        = ["roles"],
    };

    /// <summary>
    /// Resolves a <see cref="CurrentUser"/> from <paramref name="principal"/>'s claims, or
    /// <c>null</c> when unauthenticated.
    /// </summary>
    public CurrentUser? Map(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var objectId = FindFirst(principal, ObjectIdClaimTypes) ?? string.Empty;

        var email = FindFirst(principal, EmailClaimTypes)
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? string.Empty;

        var displayName = FindFirst(principal, DisplayNameClaimTypes)
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? email;

        var roles = RoleClaimTypes
            .SelectMany(principal.FindAll)
            .Concat(principal.FindAll(ClaimTypes.Role))
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        return new CurrentUser(objectId, email, displayName, roles);
    }

    private static string? FindFirst(ClaimsPrincipal principal, IReadOnlyList<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (value is not null) return value;
        }
        return null;
    }
}
