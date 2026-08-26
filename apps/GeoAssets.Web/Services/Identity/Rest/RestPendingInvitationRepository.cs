using System.Net;
using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IPendingInvitationRepository"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/invitations</c> endpoints (XD01-69).
///
/// <see cref="GetAllPendingAsync"/> and <see cref="GetByIdAsync"/> both use the admin-facing
/// <c>GET /invitations</c> list (<see cref="GetByIdAsync"/> filters it client-side rather than
/// requiring a dedicated single-item server endpoint — the list is expected to stay small).
/// Because the list only ever contains <see cref="InvitationStatus.Pending"/> rows, it
/// correctly (and usefully) returns <see langword="null"/> once an invitation has been redeemed
/// or revoked — exactly the signal XD01-71's redirect gate needs to stop firing after
/// redemption.
///
/// <see cref="GetByExternalObjectIdAsync"/> is different (XD01-92): the admin list requires
/// <c>users:read</c>, a permission a just-invited, not-yet-provisioned caller never has by
/// design (XD01-19) — so <c>InvitationRedirectGate</c>'s redirect check (XD01-89), the only
/// real caller of this method, could never actually succeed for the population it exists to
/// serve.
/// This method now calls the self-service <c>GET /invitations/me</c> endpoint instead, which
/// resolves "my own pending invitation" from the caller's own authenticated identity
/// server-side. It therefore ignores its own <paramref name="externalObjectId"/> parameter — a
/// documented assumption, not an oversight: every current and expected caller already passes
/// the caller's own id (resolved from the same <c>ICurrentUserAccessor</c> right before calling
/// this), so there is no case today where "my own" and "the id I asked for" would ever differ.
/// A hypothetical future caller passing someone *else's* id would silently get their own
/// invitation back instead of the one asked for, not the other caller's — never a leak, but
/// worth knowing if this method is ever reused for something other than the redirect gate.
///
/// Creating, revoking, and redeeming an invitation are orchestrated, multi-step server
/// operations (<c>POST /invitations</c>, <c>DELETE /invitations/{id}</c>,
/// <c>POST /invitations/{id}/redeem</c>) — not plain CRUD — so they aren't exposed through this
/// repository interface at all; XD01-71's UI calls those endpoints directly instead.
/// <see cref="AddAsync"/>/<see cref="UpdateAsync"/> therefore throw
/// <see cref="NotSupportedException"/>, the same idiom <see cref="RestUserRepository"/> uses for
/// operations its server surface doesn't expose as raw CRUD.
/// </summary>
public sealed class RestPendingInvitationRepository(HttpClient http) : IPendingInvitationRepository
{
    public async Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var invitations = await GetAllPendingAsync(ct);
        return invitations.FirstOrDefault(i => i.Id == id);
    }

    public async Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default)
    {
        var response = await http.GetAsync("invitations/me", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<PendingInvitationDto>(ct);
        return dto is null ? null : ToInvitation(dto);
    }

    public async Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default)
    {
        var dtos = await http.GetFromJsonAsync<List<PendingInvitationDto>>("invitations", ct) ?? [];
        return dtos.Select(ToInvitation).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static PendingInvitation ToInvitation(PendingInvitationDto dto) => new()
    {
        Id               = dto.Id,
        Email            = dto.Email,
        ExternalObjectId = dto.ExternalObjectId,
        InvitedByUserId  = dto.InvitedByUserId,
        InvitedAt        = dto.InvitedAt,
        RedeemedAt       = dto.RedeemedAt,
        Status           = dto.Status,
    };

    public Task AddAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
}
