using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFPendingInvitationRepository(GeoIdentityDbContext db) : IPendingInvitationRepository
{
    public Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.PendingInvitations.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default)
        => db.PendingInvitations.FirstOrDefaultAsync(i => i.ExternalObjectId == externalObjectId, ct);

    public async Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default)
        => await db.PendingInvitations.Where(i => i.Status == InvitationStatus.Pending).ToListAsync(ct);

    public async Task AddAsync(PendingInvitation invitation, CancellationToken ct = default)
        => await db.PendingInvitations.AddAsync(invitation, ct);

    public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default)
    {
        db.PendingInvitations.Update(invitation);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
