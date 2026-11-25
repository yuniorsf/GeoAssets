namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Client-facing administration of the roles users of contracted organizations hold on an organization (XD01-327) —
/// <c>GeoAssets.Server</c>'s <c>{prefix}/organizations/{orgId}/scoped-roles</c>, <c>…/contracted-users</c>,
/// <c>…/issued-grants</c> (read, and since XD01-411 written by the organization's administrators) and
/// <c>{prefix}/users/{id}/scoped-roles</c>. The server decides every rule (who administers the organization, who is eligible,
/// no self-assignment, which grants are read-only); a refusal surfaces as <see cref="ScopedRoleAdministrationException"/>,
/// its code one of <see cref="ScopedRoleAssignmentProblems"/> or <see cref="IssuedGrantProblems"/>.
///
/// Registered only by the REST-backed identity stack (<c>AddGeoIdentityRest</c>); callers resolve it optionally, the same
/// pattern as <see cref="IInvitationClient"/>.
/// </summary>
public interface IScopedRoleAdministrationClient
{
    /// <summary>The roles held on <paramref name="organizationId"/> by users of other organizations.</summary>
    Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetAssignmentsAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>The active users of the organizations holding an active, unexpired grant from <paramref name="organizationId"/>.</summary>
    Task<IReadOnlyList<ContractedUserDto>> GetContractedUsersAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>The active, unexpired grants <paramref name="organizationId"/> has issued to other organizations.</summary>
    Task<IReadOnlyList<OrganizationGrantDto>> GetIssuedGrantsAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Issues the grant <paramref name="request"/> describes from <paramref name="organizationId"/> (XD01-411); returns it.</summary>
    Task<OrganizationGrantDto> IssueGrantAsync(Guid organizationId, IssuedGrantCreateRequest request, CancellationToken ct = default);

    /// <summary>Replaces the rules of the grant <paramref name="grantId"/> <paramref name="organizationId"/> issued (XD01-411); returns it.</summary>
    Task<OrganizationGrantDto> UpdateGrantAsync(Guid organizationId, Guid grantId, IssuedGrantRules rules, CancellationToken ct = default);

    /// <summary>Revokes the grant <paramref name="grantId"/> <paramref name="organizationId"/> issued (XD01-411).</summary>
    Task RevokeGrantAsync(Guid organizationId, Guid grantId, CancellationToken ct = default);

    /// <summary>Gives <paramref name="userId"/> the role <paramref name="roleId"/> on <paramref name="organizationId"/>; returns the assignment.</summary>
    Task<ScopedRoleAssignmentDto> AssignAsync(Guid organizationId, Guid userId, Guid roleId, CancellationToken ct = default);

    /// <summary>Revokes the assignment <paramref name="assignmentId"/> held on <paramref name="organizationId"/>.</summary>
    Task RevokeAsync(Guid organizationId, Guid assignmentId, CancellationToken ct = default);

    /// <summary>The roles <paramref name="userId"/> holds on organizations other than their own, as far as the caller may see them.</summary>
    Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetUserAssignmentsAsync(Guid userId, CancellationToken ct = default);
}
