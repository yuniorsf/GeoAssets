using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IGroupRepository"/> backed by <c>GeoAssets.Server</c>'s <c>/api/identity/groups</c>
/// endpoints (XD01-128). See <see cref="RestRoleRepository"/>'s doc comment for the DTO-mapping
/// and no-op <see cref="SaveChangesAsync"/> rationale — the same applies here.
///
/// <see cref="GetByOrganizationAsync"/> and <see cref="GetGroupsForUserAsync"/> have no matching
/// server endpoint and throw <see cref="NotSupportedException"/>.
/// </summary>
public sealed class RestGroupRepository(HttpClient http) : IGroupRepository
{
    public async Task<AppGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"groups/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<GroupDto>(ct)
            ?? throw new InvalidOperationException($"GET groups/{id} returned an empty response.");
        return ToGroup(dto);
    }

    public async Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<GroupDto>>("groups", ct) ?? [];
        return dtos.Select(ToGroup).ToList();
    }

    public async Task<IReadOnlyList<AppUser>> GetMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<UserSummaryDto>>($"groups/{groupId}/members", ct) ?? [];
        return dtos.Select(ToUser).ToList();
    }

    /// <summary>
    /// The server always mints its own <c>Id</c> for a new group (see
    /// <c>IdentityRestApiExtensions</c>'s <c>POST /groups</c> — <see cref="GroupWriteDto"/> carries
    /// no <c>Id</c> field). Same Location-header pattern as <see cref="RestRoleRepository.AddAsync"/>.
    /// </summary>
    public async Task AddAsync(AppGroup group, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("groups",
            new GroupWriteDto(group.Name, group.Description, group.OrganizationId, group.IsActive), ct);
        response.EnsureSuccessStatusCode();

        var location = response.Headers.Location
            ?? throw new InvalidOperationException("POST groups did not return a Location header.");
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        group.Id = Guid.Parse(path.TrimEnd('/').Split('/')[^1]);
    }

    public async Task UpdateAsync(AppGroup group, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"groups/{group.Id}",
            new GroupWriteDto(group.Name, group.Description, group.OrganizationId, group.IsActive), ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// <paramref name="addedBy"/> is accepted for interface compliance but not sent — the server
    /// always records the authenticated caller's own id for <c>UserGroup.AddedBy</c>, the same
    /// caller-not-client-supplied-identity reasoning as <c>invitations</c>'
    /// <c>InvitedByUserId</c>.
    /// </summary>
    public async Task AddMemberAsync(Guid groupId, Guid userId, string? addedBy = null, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"groups/{groupId}/members/{userId}", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"groups/{groupId}/members/{userId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static AppGroup ToGroup(GroupDto dto) => new()
    {
        Id             = dto.Id,
        Name           = dto.Name,
        Description    = dto.Description,
        OrganizationId = dto.OrganizationId,
        IsActive       = dto.IsActive,
        CreatedAt      = dto.CreatedAt,
    };

    // UserSummaryDto has no CreatedAt — CreatedAt = default mirrors RestUserRepository's own
    // ToUser(UserSummaryDto) mapping.
    private static AppUser ToUser(UserSummaryDto dto) => new()
    {
        Id             = dto.Id,
        Email          = dto.Email,
        DisplayName    = dto.DisplayName,
        IsActive       = dto.IsActive,
        OrganizationId = dto.OrganizationId,
        CreatedAt      = default,
    };

    public Task<IReadOnlyList<AppGroup>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppGroup>> GetGroupsForUserAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
}
