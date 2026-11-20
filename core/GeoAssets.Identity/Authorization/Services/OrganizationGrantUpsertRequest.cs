namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Body of <c>PUT {prefix}/organization-grants</c> (XD01-385): the grant the caller wants to exist, keyed by
/// <see cref="Key"/>. Idempotent — the kernel creates it when no active grant has that key, brings the one that has it to
/// these <see cref="AllowedActions"/>, <see cref="RequiredRole"/> and <see cref="ExpiresAt"/> otherwise, and changes nothing
/// when it already matches. <see cref="GrantedBy"/> is recorded on creation only.
/// </summary>
public sealed record OrganizationGrantUpsertRequest(
    OrganizationGrantKey  Key,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    DateTime?             ExpiresAt,
    string                GrantedBy)
{
    /// <summary>Why this request can't be applied, or null when it can.</summary>
    public string? FindProblem()
    {
        if (Key is null) return "key is required.";
        if (Key.FindProblem() is { } problem) return problem;
        if (AllowedActions is null || AllowedActions.Count == 0) return "allowedActions must name at least one permission code.";
        if (AllowedActions.Any(string.IsNullOrWhiteSpace)) return "allowedActions must not contain a blank permission code.";
        if (RequiredRole is not null && string.IsNullOrWhiteSpace(RequiredRole)) return "requiredRole, when given, must not be blank.";
        if (string.IsNullOrWhiteSpace(GrantedBy)) return "grantedBy is required.";
        return null;
    }
}
