using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Web.Services.Identity;

namespace GeoAssets.Web.Services.Identity.InMemory;

/// <summary>
/// Parity-only stub — invitations require the Server-only Graph/ACS credentials (XD01-65), so
/// this is never functionally reachable in Blazor WASM, same reasoning as
/// <c>IRoleAssignmentProvider</c> not being registered under <c>Identity:Backend=InMemory</c>.
/// </summary>
public sealed class InMemoryPendingInvitationRepository(WasmIdentityStore store) : IPendingInvitationRepository
{
    public Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.PendingInvitations.FirstOrDefault(i => i.Id == id));

    public Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default)
        => Task.FromResult(store.PendingInvitations.FirstOrDefault(i => i.ExternalObjectId == externalObjectId));

    public Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PendingInvitation>>(
            store.PendingInvitations.Where(i => i.Status == InvitationStatus.Pending).ToList());

    public Task AddAsync(PendingInvitation invitation, CancellationToken ct = default)
    {
        store.PendingInvitations.Add(invitation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default)
    {
        var idx = store.PendingInvitations.FindIndex(i => i.Id == invitation.Id);
        if (idx >= 0) store.PendingInvitations[idx] = invitation;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
