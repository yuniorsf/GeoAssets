using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>XD01-327: an in-memory <see cref="IScopedRoleAdministrationClient"/> whose answers and refusals a test sets.</summary>
internal sealed class FakeScopedRoleClient : IScopedRoleAdministrationClient
{
    public List<ScopedRoleAssignmentDto> Assignments { get; } = [];
    public List<ContractedUserDto> ContractedUsers { get; } = [];
    public List<OrganizationGrantDto> IssuedGrants { get; } = [];
    public List<ScopedRoleAssignmentDto> UserAssignments { get; } = [];

    public List<(Guid OrganizationId, Guid UserId, Guid RoleId)> Assigned { get; } = [];
    public List<(Guid OrganizationId, Guid AssignmentId)> Revoked { get; } = [];

    public List<(Guid OrganizationId, IssuedGrantCreateRequest Request)> Issued { get; } = [];
    public List<(Guid OrganizationId, Guid GrantId, IssuedGrantRules Rules)> Updated { get; } = [];
    public List<(Guid OrganizationId, Guid GrantId)> RevokedGrants { get; } = [];

    /// <summary>Thrown by <see cref="IssueGrantAsync"/> and <see cref="UpdateGrantAsync"/> when set.</summary>
    public Exception? GrantWriteFailure { get; set; }

    public Exception? GrantRevokeFailure { get; set; }

    /// <summary>Thrown by every read when set.</summary>
    public Exception? ReadFailure { get; set; }

    public Exception? AssignFailure { get; set; }

    public Exception? RevokeFailure { get; set; }

    public Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetAssignmentsAsync(Guid organizationId, CancellationToken ct = default) =>
        Read(Assignments);

    public Task<IReadOnlyList<ContractedUserDto>> GetContractedUsersAsync(Guid organizationId, CancellationToken ct = default) =>
        Read(ContractedUsers);

    public Task<IReadOnlyList<OrganizationGrantDto>> GetIssuedGrantsAsync(Guid organizationId, CancellationToken ct = default) =>
        Read(IssuedGrants);

    public Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetUserAssignmentsAsync(Guid userId, CancellationToken ct = default) =>
        Read(UserAssignments);

    public Task<ScopedRoleAssignmentDto> AssignAsync(Guid organizationId, Guid userId, Guid roleId, CancellationToken ct = default)
    {
        if (AssignFailure is not null) return Task.FromException<ScopedRoleAssignmentDto>(AssignFailure);

        Assigned.Add((organizationId, userId, roleId));
        var user = ContractedUsers.Single(u => u.Id == userId);
        var assignment = Assignment(organizationId, user.DisplayName, user.Email, user.OrganizationName ?? string.Empty, "Contractor C1", userId, roleId);
        Assignments.Add(assignment);
        return Task.FromResult(assignment);
    }

    public Task RevokeAsync(Guid organizationId, Guid assignmentId, CancellationToken ct = default)
    {
        if (RevokeFailure is not null) return Task.FromException(RevokeFailure);

        Revoked.Add((organizationId, assignmentId));
        Assignments.RemoveAll(a => a.Id == assignmentId);
        return Task.CompletedTask;
    }

    public Task<OrganizationGrantDto> IssueGrantAsync(Guid organizationId, IssuedGrantCreateRequest request, CancellationToken ct = default)
    {
        if (GrantWriteFailure is not null) return Task.FromException<OrganizationGrantDto>(GrantWriteFailure);

        Issued.Add((organizationId, request));
        var grant = new OrganizationGrantDto(
            Guid.NewGuid(), request.GranteeOrganizationId, organizationId, request.ResourceType, request.ScopeId,
            [.. request.Rules.AllowedActions], request.Rules.RequiredRole, request.Rules.ExpiresAt, "admin@coelba.test",
            new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc), true)
        {
            RequiresScopedRole = request.Rules.RequiresScopedRole,
            OrderTypeIds = [.. request.Rules.OrderTypeIds],
            Source = "administrator",
        };
        IssuedGrants.Add(grant);
        return Task.FromResult(grant);
    }

    public Task<OrganizationGrantDto> UpdateGrantAsync(Guid organizationId, Guid grantId, IssuedGrantRules rules, CancellationToken ct = default)
    {
        if (GrantWriteFailure is not null) return Task.FromException<OrganizationGrantDto>(GrantWriteFailure);

        Updated.Add((organizationId, grantId, rules));
        var index = IssuedGrants.FindIndex(g => g.Id == grantId);
        var grant = IssuedGrants[index] with
        {
            AllowedActions = [.. rules.AllowedActions],
            RequiredRole = rules.RequiredRole,
            ExpiresAt = rules.ExpiresAt,
            RequiresScopedRole = rules.RequiresScopedRole,
            OrderTypeIds = [.. rules.OrderTypeIds],
        };
        IssuedGrants[index] = grant;
        return Task.FromResult(grant);
    }

    public Task RevokeGrantAsync(Guid organizationId, Guid grantId, CancellationToken ct = default)
    {
        if (GrantRevokeFailure is not null) return Task.FromException(GrantRevokeFailure);

        RevokedGrants.Add((organizationId, grantId));
        IssuedGrants.RemoveAll(g => g.Id == grantId);
        return Task.CompletedTask;
    }

    private Task<IReadOnlyList<T>> Read<T>(List<T> items) =>
        ReadFailure is not null ? Task.FromException<IReadOnlyList<T>>(ReadFailure) : Task.FromResult<IReadOnlyList<T>>([.. items]);

    public static ScopedRoleAssignmentDto Assignment(
        Guid organizationId, string userName, string email, string userOrganization, string role, Guid? userId = null, Guid? roleId = null,
        string organizationName = "COELBA") => new(
        Guid.NewGuid(), userId ?? Guid.NewGuid(), userName, email, Guid.NewGuid(), userOrganization, organizationId, organizationName,
        roleId ?? Guid.NewGuid(), role, "admin@coelba.test", new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc));
}
