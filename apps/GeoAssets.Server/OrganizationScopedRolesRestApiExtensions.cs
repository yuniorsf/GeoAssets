using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Administration of the roles users of contracted organizations hold on an organization (XD01-327, third slice of
/// XD01-324) — the <c>UserRole.OrganizationId</c> assignments of XD01-325, which a <c>RequiresScopedRole</c> grant
/// (XD01-326) is usable through:
/// <list type="bullet">
///   <item><c>GET {prefix}/organizations/{orgId}/scoped-roles</c> — the assignments on <c>orgId</c>;</item>
///   <item><c>POST {prefix}/organizations/{orgId}/scoped-roles</c> — assigns <see cref="ScopedRoleAssignRequest"/>;</item>
///   <item><c>DELETE {prefix}/organizations/{orgId}/scoped-roles/{assignmentId}</c> — revokes one;</item>
///   <item><c>GET {prefix}/organizations/{orgId}/contracted-users</c> — the users eligible for one: the active users of the
///   organizations holding an active, unexpired grant from <c>orgId</c>;</item>
///   <item><c>GET {prefix}/organizations/{orgId}/issued-grants</c> — those grants, with their <c>RequiresScopedRole</c> and
///   <c>OrderTypeIds</c>, so the administrator sees which contracts a role on <c>orgId</c> is needed for;</item>
///   <item><c>GET {prefix}/users/{id}/scoped-roles</c> — the roles a user holds on organizations other than their own.</item>
/// </list>
/// The organization routes are refused (403, <see cref="ScopedRoleAssignmentProblems.Forbidden"/>) unless the caller
/// administers <c>orgId</c>: it is the caller's own organization, or the caller is a platform administrator
/// (<see cref="PlatformAdministratorPermission"/>). Both are home permissions; a role held on <c>orgId</c> never makes its
/// holder an administrator of it. On top of that the route policies ask for the permissions of the existing role
/// administration: <c>users:read</c> to read, <c>users:edit</c> to assign or revoke. An assignment is refused when it names
/// the caller (400) or a user who is inactive or whose organization holds no active, unexpired grant from <c>orgId</c>
/// (409, <see cref="ScopedRoleAssignmentProblems.NotContracted"/>). Who assigned it and when are the server's: the caller,
/// and <see cref="TimeProvider"/> through the repository. A revocation takes effect on the holder's next request, since the
/// authorization context is built per request.
/// </summary>
public static class OrganizationScopedRolesRestApiExtensions
{
    /// <summary>The home permission of a platform administrator, who may administer the scoped roles of any organization.</summary>
    public const string PlatformAdministratorPermission = "organizations:manage-all";

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapOrganizationScopedRolesApi(this IEndpointRouteBuilder routes, string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        MapAssignmentReads(routes, prefix);
        MapAssignmentWrites(routes, prefix);
        MapEligibilityReads(routes, prefix);

        return routes;
    }

    // ── Assignments ──────────────────────────────────────────────────────────

    private static void MapAssignmentReads(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/organizations/{{orgId}}/scoped-roles", async (
            Guid orgId, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct).ConfigureAwait(false) is { } refusal) return refusal;

            var assignments = await s.Users.GetRoleAssignmentsOnOrganizationAsync(orgId, ct).ConfigureAwait(false);
            var names = await OrganizationNamesAsync(s.Organizations, ct).ConfigureAwait(false);
            return Results.Json(
                assignments.Select(a => ToDto(a, a.User, names)).OrderBy(a => a.UserDisplayName, StringComparer.CurrentCultureIgnoreCase),
                _opts);
        }).RequireAuthorization("users:read");

        routes.MapGet($"{prefix}/users/{{id}}/scoped-roles", async (
            Guid id, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            var user = await s.Users.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (user is null) return Problem(StatusCodes.Status404NotFound, ScopedRoleAssignmentProblems.NotFound, "The user does not exist.");

            var assignments = await s.Users.GetScopedRoleAssignmentsAsync(id, ct).ConfigureAwait(false);
            var names = await OrganizationNamesAsync(s.Organizations, ct).ConfigureAwait(false);
            return Results.Json(assignments.Where(a => MaySee(ctx, user, a)).Select(a => ToDto(a, user, names)), _opts);
        }).RequireAuthorization("users:read");
    }

    private static void MapAssignmentWrites(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/organizations/{{orgId}}/scoped-roles", async (
            Guid orgId, ScopedRoleAssignRequest? request, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct).ConfigureAwait(false) is { } refusal) return refusal;

            return await AssignAsync(ctx, orgId, request, s, ct).ConfigureAwait(false);
        }).RequireAuthorization("users:edit");

        routes.MapDelete($"{prefix}/organizations/{{orgId}}/scoped-roles/{{assignmentId}}", async (
            Guid orgId, Guid assignmentId, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct).ConfigureAwait(false) is { } refusal) return refusal;

            var assignments = await s.Users.GetRoleAssignmentsOnOrganizationAsync(orgId, ct).ConfigureAwait(false);
            var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment is null)
                return Problem(StatusCodes.Status404NotFound, ScopedRoleAssignmentProblems.NotFound, "No such assignment on this organization.");

            await s.Users.RemoveRoleOnOrganizationAsync(assignment.UserId, assignment.RoleId, orgId, ct).ConfigureAwait(false);
            await s.Users.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("users:edit");
    }

    private static async Task<IResult> AssignAsync(
        AuthorizationContext ctx, Guid orgId, ScopedRoleAssignRequest? request, ScopedRoleServices s, CancellationToken ct)
    {
        if (request is null || request.UserId == Guid.Empty || request.RoleId == Guid.Empty)
            return Problem(StatusCodes.Status400BadRequest, ScopedRoleAssignmentProblems.Invalid, "userId and roleId are required.");
        if (request.UserId == ctx.User.Id)
            return Problem(StatusCodes.Status400BadRequest, ScopedRoleAssignmentProblems.SelfAssignment, "You cannot assign a role to yourself.");

        var role = await s.Roles.GetByIdAsync(request.RoleId, ct).ConfigureAwait(false);
        if (role is null)
            return Problem(StatusCodes.Status400BadRequest, ScopedRoleAssignmentProblems.Invalid, "The role does not exist.");

        var user = await s.Users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null)
            return Problem(StatusCodes.Status404NotFound, ScopedRoleAssignmentProblems.NotFound, "The user does not exist.");

        if (await FindIneligibilityAsync(user, orgId, s.Grants, ct).ConfigureAwait(false) is { } reason)
            return Problem(StatusCodes.Status409Conflict, ScopedRoleAssignmentProblems.NotContracted, reason);

        var existed = (await s.Users.GetRoleAssignmentsOnOrganizationAsync(orgId, ct).ConfigureAwait(false))
            .Any(a => a.UserId == user.Id && a.RoleId == role.Id);
        if (!existed)
        {
            await s.Users.AssignRoleOnOrganizationAsync(user.Id, role.Id, orgId, AssignedBy(ctx), ct).ConfigureAwait(false);
            await s.Users.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var assignment = (await s.Users.GetRoleAssignmentsOnOrganizationAsync(orgId, ct).ConfigureAwait(false))
            .First(a => a.UserId == user.Id && a.RoleId == role.Id);
        var dto = ToDto(assignment, assignment.User, await OrganizationNamesAsync(s.Organizations, ct).ConfigureAwait(false));
        return Results.Json(dto, _opts, statusCode: existed ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    // ── Eligibility ──────────────────────────────────────────────────────────

    private static void MapEligibilityReads(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/organizations/{{orgId}}/contracted-users", async (
            Guid orgId, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct).ConfigureAwait(false) is { } refusal) return refusal;

            var grants = await s.Grants.GetActiveGrantsIssuedByAsync(orgId, s.Time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
            var names = await OrganizationNamesAsync(s.Organizations, ct).ConfigureAwait(false);

            var eligible = new List<ContractedUserDto>();
            foreach (var granteeId in grants.Select(g => g.GranteeOrganizationId).Where(g => g != orgId).Distinct())
            {
                var members = await s.Users.GetByOrganizationAsync(granteeId, ct).ConfigureAwait(false);
                eligible.AddRange(members
                    .Where(u => u.IsActive && u.Id != ctx.User.Id)
                    .Select(u => new ContractedUserDto(u.Id, u.DisplayName, u.Email, granteeId, names.GetValueOrDefault(granteeId))));
            }

            return Results.Json(eligible
                .OrderBy(u => u.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase), _opts);
        }).RequireAuthorization("users:read");

        routes.MapGet($"{prefix}/organizations/{{orgId}}/issued-grants", async (
            Guid orgId, [AsParameters] ScopedRoleServices s, CancellationToken ct) =>
        {
            var ctx = await s.Authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (await RefuseUnlessAdministratorAsync(ctx, orgId, s.Organizations, ct).ConfigureAwait(false) is { } refusal) return refusal;

            var grants = await s.Grants.GetActiveGrantsIssuedByAsync(orgId, s.Time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
            return Results.Json(grants.Where(g => g.GranteeOrganizationId != orgId).Select(OrganizationGrantDto.FromModel), _opts);
        }).RequireAuthorization("users:read");
    }

    /// <summary>
    /// Why <paramref name="user"/> may not hold a role on <paramref name="organizationId"/>, or null when they may: they are
    /// active and their organization — another one — holds an active, unexpired grant from it.
    /// </summary>
    private static async Task<string?> FindIneligibilityAsync(
        AppUser user, Guid organizationId, IOrganizationGrantRepository grantRepo, CancellationToken ct)
    {
        if (!user.IsActive) return "The user is inactive.";
        if (user.OrganizationId is not { } home) return "The user belongs to no organization.";
        if (home == organizationId) return "The user is a member of this organization; their roles here are their home roles.";

        var grants = await grantRepo.GetActiveGrantsAsync(home, organizationId, ct).ConfigureAwait(false);
        return grants.Count == 0
            ? "The user's organization holds no active grant from this organization."
            : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsPlatformAdministrator(AuthorizationContext ctx) => ctx.HasPermission(PlatformAdministratorPermission);

    /// <summary>
    /// Null when the caller administers <paramref name="organizationId"/> (their own organization, or any existing one for a
    /// platform administrator); otherwise the refusal to answer.
    /// </summary>
    internal static async Task<IResult?> RefuseUnlessAdministratorAsync(
        AuthorizationContext ctx, Guid organizationId, IOrganizationRepository orgRepo, CancellationToken ct)
    {
        if (IsPlatformAdministrator(ctx))
        {
            return await orgRepo.GetByIdAsync(organizationId, ct).ConfigureAwait(false) is null
                ? Problem(StatusCodes.Status404NotFound, ScopedRoleAssignmentProblems.NotFound, "The organization does not exist.")
                : null;
        }

        return ctx.User.OrganizationId == organizationId
            ? null
            : Problem(StatusCodes.Status403Forbidden, ScopedRoleAssignmentProblems.Forbidden, "You can administer only your own organization.");
    }

    /// <summary>
    /// A platform administrator and the members of <paramref name="user"/>'s own organization see every role the user holds
    /// elsewhere; anyone else only the roles held on their own organization, so no caller learns another pair's contract.
    /// </summary>
    private static bool MaySee(AuthorizationContext ctx, AppUser user, UserRole assignment)
    {
        if (IsPlatformAdministrator(ctx)) return true;
        if (ctx.User.OrganizationId is not { } callerOrganization) return false;
        return user.OrganizationId == callerOrganization || assignment.OrganizationId == callerOrganization;
    }

    internal static string AssignedBy(AuthorizationContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.User.Email) ? ctx.User.Id.ToString() : ctx.User.Email;

    private static async Task<Dictionary<Guid, string>> OrganizationNamesAsync(IOrganizationRepository orgRepo, CancellationToken ct)
    {
        var organizations = await orgRepo.GetAllAsync(ct).ConfigureAwait(false);
        return organizations.ToDictionary(o => o.Id, o => o.Name);
    }

    private static ScopedRoleAssignmentDto ToDto(UserRole assignment, AppUser user, Dictionary<Guid, string> organizationNames)
    {
        var organizationId = assignment.OrganizationId.GetValueOrDefault();
        return new ScopedRoleAssignmentDto(
            Id:                   assignment.Id,
            UserId:               assignment.UserId,
            UserDisplayName:      user.DisplayName,
            UserEmail:            user.Email,
            UserOrganizationId:   user.OrganizationId,
            UserOrganizationName: user.OrganizationId is { } home ? organizationNames.GetValueOrDefault(home) : null,
            OrganizationId:       organizationId,
            OrganizationName:     organizationNames.GetValueOrDefault(organizationId),
            RoleId:               assignment.RoleId,
            RoleName:             assignment.Role.Name,
            AssignedBy:           assignment.AssignedBy,
            AssignedAt:           assignment.AssignedAt);
    }

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: "The scoped-role request was refused.",
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

    /// <summary>The services every route of this API reads, bound from DI by <c>[AsParameters]</c>.</summary>
    internal sealed record ScopedRoleServices(
        IGeoAuthorizationService     Authorization,
        IUserRepository              Users,
        IRoleRepository              Roles,
        IOrganizationRepository      Organizations,
        IOrganizationGrantRepository Grants,
        TimeProvider                 Time);
}
