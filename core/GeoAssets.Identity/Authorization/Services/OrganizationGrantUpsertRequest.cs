namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Body of <c>PUT {prefix}/organization-grants</c> (XD01-385): the grant the caller wants to exist, keyed by
/// <see cref="Key"/>. Idempotent — the kernel creates it when no active grant has that key, brings the one that has it to
/// these <see cref="AllowedActions"/>, <see cref="RequiredRole"/>, <see cref="ExpiresAt"/>, <see cref="RequiresScopedRole"/> and
/// <see cref="OrderTypeIds"/> otherwise, and changes nothing when it already matches. <see cref="GrantedBy"/> is recorded on
/// creation only.
/// </summary>
public sealed record OrganizationGrantUpsertRequest(
    OrganizationGrantKey  Key,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    DateTime?             ExpiresAt,
    string                GrantedBy)
{
    /// <summary>The service-order resource type, the only one <see cref="OrderTypeIds"/> may narrow (XD01-326).</summary>
    public const string ServiceOrderResourceType = "ServiceOrder";

    /// <summary>
    /// <c>OrganizationGrant.RequiresScopedRole</c> (XD01-326): true makes the grant usable only through roles held on the
    /// resource organization. False when absent — the organization-wide semantics every earlier caller relies on.
    /// </summary>
    public bool RequiresScopedRole { get; init; }

    /// <summary>
    /// <c>OrganizationGrant.OrderTypeIds</c> (XD01-326): the service-order types the grant is narrowed to; empty (the
    /// default when absent) means every order type. Only accepted with <see cref="ServiceOrderResourceType"/>.
    /// </summary>
    public IReadOnlyList<string> OrderTypeIds { get; init; } = [];

    /// <summary>Why this request can't be applied, or null when it can.</summary>
    public string? FindProblem()
    {
        if (Key is null) return "key is required.";
        if (Key.FindProblem() is { } problem) return problem;
        if (AllowedActions is null || AllowedActions.Count == 0) return "allowedActions must name at least one permission code.";
        if (AllowedActions.Any(string.IsNullOrWhiteSpace)) return "allowedActions must not contain a blank permission code.";
        if (RequiredRole is not null && string.IsNullOrWhiteSpace(RequiredRole)) return "requiredRole, when given, must not be blank.";
        if (string.IsNullOrWhiteSpace(GrantedBy)) return "grantedBy is required.";
        if (OrderTypeIds is null) return "orderTypeIds, when given, must be a list.";
        if (OrderTypeIds.Any(string.IsNullOrWhiteSpace)) return "orderTypeIds must not contain a blank order type id.";
        if (OrderTypeIds.Count > 0 && !string.Equals(Key.ResourceType, ServiceOrderResourceType, StringComparison.Ordinal))
            return $"orderTypeIds may only narrow a grant whose resourceType is \"{ServiceOrderResourceType}\".";
        return null;
    }
}
