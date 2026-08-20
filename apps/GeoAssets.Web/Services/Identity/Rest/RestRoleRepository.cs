using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IRoleRepository"/> backed by <c>GeoAssets.Server</c>'s <c>/api/identity/roles</c>
/// endpoints (XD01-56). See <see cref="RestUserRepository"/>'s doc comment for the DTO-mapping
/// and no-op <see cref="SaveChangesAsync"/> rationale — the same applies here.
///
/// <see cref="GetPermissionsAsync"/> and <see cref="GetByNameAsync"/> have no matching server
/// endpoint (a role's permission IDs are only available via <see cref="GetByIdAsync"/>'s
/// <see cref="RoleDetailDto.PermissionIds"/>, a different shape) and throw
/// <see cref="NotSupportedException"/>.
/// </summary>
public sealed class RestRoleRepository(HttpClient http) : IRoleRepository
{
    public async Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"roles/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<RoleDetailDto>(ct)
            ?? throw new InvalidOperationException($"GET roles/{id} returned an empty response.");
        return ToRole(dto);
    }

    public async Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<RoleSummaryDto>>("roles", ct) ?? [];
        return dtos.Select(ToRole).ToList();
    }

    /// <summary>
    /// The server always mints its own <c>Id</c> for a new role (see
    /// <c>IdentityRestApiExtensions</c>'s <c>POST /roles</c> — it never accepts a client-supplied
    /// one, since <see cref="Services.RoleWriteDto"/> carries no <c>Id</c> field). Whatever
    /// <paramref name="role"/><c>.Id</c> was set to before this call is therefore overwritten
    /// from the <c>Location</c> response header on success, so the caller's object reflects the
    /// actually-persisted identity afterward.
    /// </summary>
    public async Task AddAsync(AppRole role, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("roles", new RoleWriteDto(role.Name, role.Description), ct);
        response.EnsureSuccessStatusCode();

        var location = response.Headers.Location
            ?? throw new InvalidOperationException("POST roles did not return a Location header.");
        // Results.Created(string, ...) writes the raw path as-is — "/api/identity/roles/{id}" —
        // which Uri parses as *relative* (no scheme/host), so .Segments (absolute-only) would
        // throw here. Split on the raw string instead to handle both relative and absolute forms.
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        role.Id = Guid.Parse(path.TrimEnd('/').Split('/')[^1]);
    }

    public async Task UpdateAsync(AppRole role, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"roles/{role.Id}", new RoleWriteDto(role.Name, role.Description), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"roles/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"roles/{roleId}/permissions/{permissionId}", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"roles/{roleId}/permissions/{permissionId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static AppRole ToRole(RoleSummaryDto dto) => new()
    {
        Id          = dto.Id,
        Name        = dto.Name,
        Description = dto.Description,
        IsBuiltIn   = dto.IsBuiltIn,
    };

    private static AppRole ToRole(RoleDetailDto dto) => new()
    {
        Id          = dto.Id,
        Name        = dto.Name,
        Description = dto.Description,
        IsBuiltIn   = dto.IsBuiltIn,
    };

    public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
}
