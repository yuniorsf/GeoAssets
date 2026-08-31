using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IInvitationClient"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/invitations</c> endpoints (XD01-69).
/// </summary>
public sealed class RestInvitationClient(HttpClient http) : IInvitationClient
{
    public async Task<PendingInvitation> CreateInvitationAsync(string email, string displayName, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("invitations", new InvitationCreateDto(email, displayName), ct);
        response.EnsureSuccessStatusCode(); // 201 (sent) or 202 (email failed, row still created) both succeed here

        var dto = await response.Content.ReadFromJsonAsync<PendingInvitationDto>(ct)
            ?? throw new InvalidOperationException("POST invitations returned an empty response.");
        return ToInvitation(dto);
    }

    public async Task RevokeInvitationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"invitations/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RedeemInvitationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"invitations/{id}/redeem", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

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
}
