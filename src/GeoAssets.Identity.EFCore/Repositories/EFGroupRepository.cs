using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFGroupRepository(GeoIdentityDbContext db) : IGroupRepository
{
    public Task<AppGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Groups.Include(g => g.UserGroups)
                    .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken ct = default)
        => await db.Groups.ToListAsync(ct);

    public async Task<IReadOnlyList<AppGroup>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await db.Groups.Where(g => g.OrganizationId == organizationId).ToListAsync(ct);

    public async Task<IReadOnlyList<AppGroup>> GetGroupsForUserAsync(Guid userId, CancellationToken ct = default)
        => await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.Group)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AppUser>> GetMembersAsync(Guid groupId, CancellationToken ct = default)
        => await db.UserGroups
            .Where(ug => ug.GroupId == groupId)
            .Select(ug => ug.User)
            .ToListAsync(ct);

    public async Task AddAsync(AppGroup group, CancellationToken ct = default)
        => await db.Groups.AddAsync(group, ct);

    public Task UpdateAsync(AppGroup group, CancellationToken ct = default)
    {
        db.Groups.Update(group);
        return Task.CompletedTask;
    }

    public async Task AddMemberAsync(Guid groupId, Guid userId, string? addedBy = null, CancellationToken ct = default)
    {
        var exists = await db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId, ct);
        if (!exists)
            await db.UserGroups.AddAsync(new UserGroup { UserId = userId, GroupId = groupId, AddedBy = addedBy }, ct);
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var ug = await db.UserGroups.FindAsync([userId, groupId], ct);
        if (ug is not null) db.UserGroups.Remove(ug);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
