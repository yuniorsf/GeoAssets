using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryGroupRepository(WasmIdentityStore store) : IGroupRepository
{
    public Task<AppGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.Groups.FirstOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppGroup>>(store.Groups.ToList());

    public Task<IReadOnlyList<AppGroup>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppGroup>>(
            store.Groups.Where(g => g.OrganizationId == organizationId).ToList());

    public Task<IReadOnlyList<AppGroup>> GetGroupsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var groupIds = store.UserGroups.Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppGroup>>(
            store.Groups.Where(g => groupIds.Contains(g.Id)).ToList());
    }

    public Task<IReadOnlyList<AppUser>> GetMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        var userIds = store.UserGroups.Where(ug => ug.GroupId == groupId).Select(ug => ug.UserId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppUser>>(
            store.Users.Where(u => userIds.Contains(u.Id)).ToList());
    }

    public Task AddAsync(AppGroup group, CancellationToken ct = default)
    {
        store.Groups.Add(group);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppGroup group, CancellationToken ct = default)
    {
        var idx = store.Groups.FindIndex(g => g.Id == group.Id);
        if (idx >= 0) store.Groups[idx] = group;
        return Task.CompletedTask;
    }

    public Task AddMemberAsync(Guid groupId, Guid userId, string? addedBy = null, CancellationToken ct = default)
    {
        if (!store.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == groupId))
            store.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId, AddedBy = addedBy });
        return Task.CompletedTask;
    }

    public Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        store.UserGroups.RemoveAll(ug => ug.UserId == userId && ug.GroupId == groupId);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
