using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IPolicyRepository"/> backed by <c>GeoAssets.Server</c>'s
/// <c>GET /api/identity/policies</c> endpoint (XD01-18) — the only one that exists. Like
/// <see cref="RestPermissionRepository"/>, policies are code-seeded and read-only by design
/// (see <c>GeoIdentitySeeder.SeedPoliciesAsync</c>); every write/lookup method here throws
/// <see cref="NotSupportedException"/>.
/// </summary>
public sealed class RestPolicyRepository(HttpClient http) : IPolicyRepository
{
    public async Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<PolicyDto>>("policies", ct) ?? [];
        return dtos.Select(ToPolicy).ToList();
    }

    private static AppPolicy ToPolicy(PolicyDto dto) => new()
    {
        Id           = dto.Id,
        Name         = dto.Name,
        Description  = dto.Description,
        Operator     = dto.Operator,
        Requirements = [.. dto.Requirements.Select(r => new PolicyRequirement
        {
            PolicyId   = dto.Id,
            Type       = r.Type,
            Value      = r.Value,
            ClaimValue = r.ClaimValue,
        })],
    };

    public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
