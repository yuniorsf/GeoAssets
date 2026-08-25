using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IPendingInvitationRepository"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/invitations</c> endpoints (XD01-69).
///
/// Only <see cref="GetAllPendingAsync"/> has a directly matching server endpoint
/// (<c>GET /invitations</c>). <see cref="GetByIdAsync"/> and
/// <see cref="GetByExternalObjectIdAsync"/> are implemented by filtering that same list
/// client-side rather than requiring a dedicated single-item server endpoint — the list is
/// admin-facing and expected to stay small. Because the list only ever contains
/// <see cref="InvitationStatus.Pending"/> rows, both correctly (and usefully) return
/// <see langword="null"/> once an invitation has been redeemed or revoked — exactly the signal
/// XD01-71's redirect gate needs to stop firing after redemption.
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
        var invitations = await GetAllPendingAsync(ct);
        return invitations.FirstOrDefault(i => i.ExternalObjectId == externalObjectId);
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
