namespace GeoAssets.Identity.Authorization.Services;

/// <summary>What an administrator may set on a grant their organization issues (XD01-411), checked the same way on create and edit.</summary>
public sealed record IssuedGrantRules(
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    bool                  RequiresScopedRole,
    IReadOnlyList<string> OrderTypeIds,
    DateTime?             ExpiresAt)
{
    /// <summary>Why these rules can't apply to a grant of <paramref name="resourceType"/>, or null when they can.</summary>
    public string? FindProblem(string? resourceType)
    {
        if (AllowedActions is null || AllowedActions.Count == 0) return "allowedActions must name at least one permission code.";
        if (AllowedActions.Any(string.IsNullOrWhiteSpace)) return "allowedActions must not contain a blank permission code.";
        if (RequiredRole is not null && string.IsNullOrWhiteSpace(RequiredRole)) return "requiredRole, when given, must not be blank.";
        if (OrderTypeIds is null) return "orderTypeIds, when given, must be a list.";
        if (OrderTypeIds.Any(string.IsNullOrWhiteSpace)) return "orderTypeIds must not contain a blank order type id.";
        if (OrderTypeIds.Count > 0
            && !string.Equals(resourceType, OrganizationGrantUpsertRequest.ServiceOrderResourceType, StringComparison.Ordinal))
            return $"orderTypeIds may only narrow a grant whose resourceType is \"{OrganizationGrantUpsertRequest.ServiceOrderResourceType}\".";
        return null;
    }
}
