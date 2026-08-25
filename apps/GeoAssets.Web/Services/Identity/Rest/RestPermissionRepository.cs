using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IPermissionRepository"/> backed by <c>GeoAssets.Server</c>'s
/// <c>GET /api/identity/permissions</c> endpoint (XD01-56) — the only one that exists.
/// Permissions are code-seeded and read-only by design (XD01-54 Phase 1); every write/lookup
/// method here throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class RestPermissionRepository(HttpClient http) : IPermissionRepository
{
    public async Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<PermissionDto>>("permissions", ct) ?? [];
        return dtos.Select(ToPermission).ToList();
    }

    private static AppPermission ToPermission(PermissionDto dto) => new()
    {
        Id          = dto.Id,
        Code        = dto.Code,
        Resource    = dto.Resource,
        Action      = dto.Action,
        Description = dto.Description,
    };

    public Task<AppPermission?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppPermission?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
