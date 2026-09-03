using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IUserRepository"/> backed by <c>GeoAssets.Server</c>'s <c>/api/identity/users</c>
/// endpoints (XD01-56). Maps <see cref="UserSummaryDto"/>/<see cref="UserDetailDto"/> — not the
/// raw domain type — to/from the wire, the same reason <see cref="RestGeoAuthorizationService"/>
/// does (EF navigation cycles make <see cref="AppUser"/> unsafe to serialize on the server side).
///
/// Only the methods with a matching server endpoint are implemented — including
/// <see cref="GetByExternalObjectIdAsync"/> (XD01-134 follow-up), which callers like
/// <c>MainLayout</c> use to resolve the current user's own organization for the topbar; that
/// endpoint requires <c>users:read</c> like the other admin lookups, so it only resolves for
/// callers with that permission. Users are JIT-provisioned on login (see <see cref="AppUser"/>'s
/// doc comment) — there is no create-user endpoint, and role *assignment* is sourced from the
/// external provider's roles claim, not this repository (XD01-19) — see XD01-54 Phase 2 for the
/// provider-backed seam. Every other method throws <see cref="NotSupportedException"/>, matching
/// the <c>FakePolicyRepository</c> idiom already used in the Server test suite for repository
/// members no caller in this codebase reaches.
///
/// Like <see cref="Workflow.Rest.RestOrderTypeRepository"/>, each write here is already fully
/// persisted server-side by the time its HTTP call returns (the server calls its own
/// <c>SaveChangesAsync</c> internally per request) — <see cref="SaveChangesAsync"/> is a no-op.
/// </summary>
public sealed class RestUserRepository(HttpClient http) : IUserRepository
{
    public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"users/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserDetailDto>(ct)
            ?? throw new InvalidOperationException($"GET users/{id} returned an empty response.");
        return ToUser(dto);
    }

    public async Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"users/by-external-id/{Uri.EscapeDataString(oid)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserDetailDto>(ct)
            ?? throw new InvalidOperationException($"GET users/by-external-id/{oid} returned an empty response.");
        return ToUser(dto);
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<UserSummaryDto>>("users", ct) ?? [];
        return dtos.Select(ToUser).ToList();
    }

    public async Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            $"users/{user.Id}", new UserUpdateDto(user.DisplayName, user.IsActive, user.OrganizationId), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static AppUser ToUser(UserDetailDto dto) => new()
    {
        Id             = dto.Id,
        Email          = dto.Email,
        DisplayName    = dto.DisplayName,
        IsActive       = dto.IsActive,
        OrganizationId = dto.OrganizationId,
        CreatedAt      = dto.CreatedAt,
        LastLoginAt    = dto.LastLoginAt,
    };

    // UserSummaryDto has no CreatedAt/LastLoginAt — CreatedAt = default mirrors how
    // RestGeoAuthorizationService maps AuthorizationContextDto (which also lacks it).
    private static AppUser ToUser(UserSummaryDto dto) => new()
    {
        Id             = dto.Id,
        Email          = dto.Email,
        DisplayName    = dto.DisplayName,
        IsActive       = dto.IsActive,
        OrganizationId = dto.OrganizationId,
        CreatedAt      = default,
    };

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppUser user, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
}
