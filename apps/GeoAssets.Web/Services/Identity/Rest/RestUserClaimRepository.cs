using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IUserClaimRepository"/> backed by <c>GeoAssets.Server</c>'s self-service
/// <c>/api/identity/userclaims</c> endpoints (XD01-87).
///
/// Self-service only — the server always resolves "which user" from the caller's own bearer
/// token, never from a client-supplied id. Every method's <paramref name="userId"/>-shaped
/// parameter is therefore informational only against this backend: passing a different id than
/// the caller's own silently has no effect (the server just operates on the caller's own claims
/// regardless), rather than erroring. Callers should always pass the current user's own id.
///
/// <see cref="GetByTypeAsync"/> (a cross-user query — "every user with claim type X") has no
/// self-service mapping and no server endpoint; throws <see cref="NotSupportedException"/>,
/// matching <see cref="RestUserRepository"/>'s idiom for operations its server surface doesn't
/// expose.
/// </summary>
public sealed class RestUserClaimRepository(HttpClient http) : IUserClaimRepository
{
    public async Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<UserClaimDto>>("userclaims", ct) ?? [];
        return dtos.Select(dto => ToClaim(dto, userId)).ToList();
    }

    public Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default) => throw new NotSupportedException();

    public async Task<UserClaim?> GetAsync(Guid userId, string claimType, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"userclaims/{Uri.EscapeDataString(claimType)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserClaimDto>(ct)
            ?? throw new InvalidOperationException($"GET userclaims/{claimType} returned an empty response.");
        return ToClaim(dto, userId);
    }

    public async Task AddAsync(UserClaim claim, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("userclaims", new UserClaimWriteDto(claim.Type, claim.Value), ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserClaimDto>(ct)
            ?? throw new InvalidOperationException("POST userclaims returned an empty response.");
        claim.Id = dto.Id;
    }

    public async Task UpdateAsync(UserClaim claim, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"userclaims/{claim.Id}", new UserClaimUpdateDto(claim.Value), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAsync(Guid claimId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"userclaims/{claimId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAllAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync("userclaims", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    // The server's self-service endpoints never report UserId back (it's always the caller's
    // own) — this is the only source of truth for that field on the mapped domain object.
    private static UserClaim ToClaim(UserClaimDto dto, Guid userId) => new()
    {
        Id     = dto.Id,
        UserId = userId,
        Type   = dto.Type,
        Value  = dto.Value,
    };
}
