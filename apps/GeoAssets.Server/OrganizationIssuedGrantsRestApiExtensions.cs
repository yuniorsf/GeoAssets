using System.Text.Json;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// The grants an organization issues to its contractors, written by its own administrators (XD01-411, follow-up of
/// XD01-327); <c>GET {prefix}/organizations/{orgId}/issued-grants</c> lists them
/// (<see cref="OrganizationScopedRolesRestApiExtensions"/>):
/// <list type="bullet">
///   <item><c>POST {prefix}/organizations/{orgId}/issued-grants</c>: issues <see cref="IssuedGrantCreateRequest"/>
///   (201 with the grant);</item>
///   <item><c>PUT {prefix}/organizations/{orgId}/issued-grants/{grantId}</c>: replaces the grant's
///   <see cref="IssuedGrantRules"/> (200 with the grant);</item>
///   <item><c>DELETE {prefix}/organizations/{orgId}/issued-grants/{grantId}</c>: revokes it (204).</item>
/// </list>
/// The caller must hold <see cref="IssueGrantsPermission"/> and administer <c>orgId</c> (their own organization, or any
/// for a platform administrator; otherwise 403 <see cref="ScopedRoleAssignmentProblems.Forbidden"/>). Who issued the grant
/// and when are the server's: the caller and <see cref="TimeProvider"/>. Refusals are <c>application/problem+json</c> with
/// an <see cref="IssuedGrantProblems"/> code: 400 when the body is invalid or names an unknown organization, permission or
/// role, or an expiry that has passed; 404 for a grant this organization has not issued or that is no longer active; 409
/// when the key already has an active grant, when the grant is owned by a vertical host (read-only here, XD01-411), or when
/// a concurrent write of the key won the race (XD01-394).
/// </summary>
public static class OrganizationIssuedGrantsRestApiExtensions
{
    /// <summary>The home permission to issue, edit and revoke the grants of one's own organization (XD01-411).</summary>
    public const string IssueGrantsPermission = "organizations:issue-grants";

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapOrganizationIssuedGrantsApi(this IEndpointRouteBuilder routes, string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        MapIssue(routes, prefix);
        MapUpdate(routes, prefix);
        MapRevoke(routes, prefix);

        return routes;
    }

    private static void MapIssue(IEndpointRouteBuilder routes, string prefix) =>
        routes.MapPost($"{prefix}/organizations/{{orgId}}/issued-grants", async (
            Guid orgId, IssuedGrantCreateRequest? request, [AsParameters] IssuedGrantServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await OrganizationScopedRolesRestApiExtensions.RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct)
                    .ConfigureAwait(false) is { } refusal) return refusal;

            if (request is null) return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, "The grant is missing.");
            if (request.FindProblem(orgId) is { } problem) return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, problem);
            if (await s.Organizations.GetByIdAsync(request.GranteeOrganizationId, ct).ConfigureAwait(false) is null)
                return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, "The grantee organization does not exist.");

            var rules = Normalized(request.Rules);
            if (await FindRulesProblemAsync(rules, s, ct).ConfigureAwait(false) is { } rulesProblem)
                return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, rulesProblem);

            try
            {
                var result = await s.Grants
                    .IssueAsync(orgId, request with { Rules = rules }, OrganizationScopedRolesRestApiExtensions.AssignedBy(ctx), ct)
                    .ConfigureAwait(false);
                return Answer(result, StatusCodes.Status201Created);
            }
            catch (OrganizationGrantKeyConflictException)
            {
                return Problem(StatusCodes.Status409Conflict, IssuedGrantProblems.Conflict,
                    "Another write of a grant with this key raced this one; repeat the request.");
            }
        }).RequireAuthorization(IssueGrantsPermission);

    private static void MapUpdate(IEndpointRouteBuilder routes, string prefix) =>
        routes.MapPut($"{prefix}/organizations/{{orgId}}/issued-grants/{{grantId}}", async (
            Guid orgId, Guid grantId, IssuedGrantUpdateRequest? request, [AsParameters] IssuedGrantServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await OrganizationScopedRolesRestApiExtensions.RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct)
                    .ConfigureAwait(false) is { } refusal) return refusal;

            if (request?.Rules is null) return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, "rules are required.");

            var rules = Normalized(request.Rules);
            var existing = await s.GrantRepository.GetByIdAsync(grantId, ct).ConfigureAwait(false);
            if (existing is null || existing.ResourceOrganizationId != orgId)
                return Answer(new IssuedGrantResult(IssuedGrantOutcome.NotFound, null), StatusCodes.Status200OK);
            if (rules.FindProblem(existing.ResourceType) is { } problem)
                return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, problem);
            if (await FindRulesProblemAsync(rules, s, ct).ConfigureAwait(false) is { } rulesProblem)
                return Problem(StatusCodes.Status400BadRequest, IssuedGrantProblems.Invalid, rulesProblem);

            var result = await s.Grants.UpdateAsync(orgId, grantId, rules, ct).ConfigureAwait(false);
            return Answer(result, StatusCodes.Status200OK);
        }).RequireAuthorization(IssueGrantsPermission);

    private static void MapRevoke(IEndpointRouteBuilder routes, string prefix) =>
        routes.MapDelete($"{prefix}/organizations/{{orgId}}/issued-grants/{{grantId}}", async (
            Guid orgId, Guid grantId, [AsParameters] IssuedGrantServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await OrganizationScopedRolesRestApiExtensions.RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct)
                    .ConfigureAwait(false) is { } refusal) return refusal;

            var result = await s.Grants.RevokeAsync(orgId, grantId, ct).ConfigureAwait(false);
            return result.Outcome == IssuedGrantOutcome.Revoked ? Results.NoContent() : Answer(result, StatusCodes.Status200OK);
        }).RequireAuthorization(IssueGrantsPermission);

    /// <summary>The rules with their expiry in UTC (an expiry without a zone is taken as UTC).</summary>
    private static IssuedGrantRules Normalized(IssuedGrantRules rules) => rules with
    {
        ExpiresAt = rules.ExpiresAt switch
        {
            null => null,
            { Kind: DateTimeKind.Unspecified } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
            { } expiresAt => expiresAt.ToUniversalTime(),
        },
    };

    /// <summary>
    /// Why <paramref name="rules"/> can't be stored, beyond their shape: an expiry that has passed (the grant would be
    /// invisible to every lookup), an action that is no permission, or a required role that is no role.
    /// </summary>
    private static async Task<string?> FindRulesProblemAsync(IssuedGrantRules rules, IssuedGrantServices s, CancellationToken ct)
    {
        if (rules.ExpiresAt is { } expiresAt && expiresAt <= s.Time.GetUtcNow().UtcDateTime) return "expiresAt must be in the future.";

        var permissions = (await s.Permissions.GetAllAsync(ct).ConfigureAwait(false))
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (rules.AllowedActions.FirstOrDefault(a => !permissions.Contains(a)) is { } unknown)
            return $"allowedActions names \"{unknown}\", which is no permission.";

        if (rules.RequiredRole is { } requiredRole
            && !(await s.Roles.GetAllAsync(ct).ConfigureAwait(false)).Any(r => string.Equals(r.Name, requiredRole, StringComparison.Ordinal)))
            return $"requiredRole names \"{requiredRole}\", which is no role.";

        return null;
    }

    private static IResult Answer(IssuedGrantResult result, int successStatus) => result.Outcome switch
    {
        IssuedGrantOutcome.NotFound => Problem(StatusCodes.Status404NotFound, IssuedGrantProblems.NotFound,
            "This organization has issued no active grant with this id."),
        IssuedGrantOutcome.Exists => Problem(StatusCodes.Status409Conflict, IssuedGrantProblems.Exists,
            "An active grant to this organization over the same resources and scope already exists; edit it instead."),
        IssuedGrantOutcome.ServiceOwned => Problem(StatusCodes.Status409Conflict, IssuedGrantProblems.ServiceOwned,
            "This grant is provisioned by an integration and can't be changed here."),
        _ => Results.Json(OrganizationGrantDto.FromModel(result.Grant!), _opts, statusCode: successStatus),
    };

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: "The issued-grant request was refused.",
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

    /// <summary>The services every route of this API reads, bound from DI by <c>[AsParameters]</c>.</summary>
    internal sealed record IssuedGrantServices(
        IGeoAuthorizationService       Authorization,
        IOrganizationRepository        Organizations,
        IOrganizationGrantRepository   GrantRepository,
        IPermissionRepository          Permissions,
        IRoleRepository                Roles,
        IssuedOrganizationGrantService Grants,
        TimeProvider                   Time);
}
