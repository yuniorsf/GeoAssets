using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

/// <summary>
/// The scoped role assignment change feed (XD01-328) over <c>ScopedRoleAssignmentSyncOutbox</c>. Each outbox row names a
/// (user, organization) pair; it is answered with what the user holds there when the batch is read — the role names and
/// their permission codes, resolved exactly as <c>GeoAuthorizationService</c> resolves <c>AuthorizationContext.ScopedRoles</c>/
/// <c>ScopedPermissions</c> (an assignment on the user's own home organization counts for nothing) — or, when it holds
/// nothing, a tombstone.
/// </summary>
public sealed class EFScopedRoleAssignmentSyncFeed(GeoIdentityDbContext db) : IScopedRoleAssignmentSyncFeed
{
    public async Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default)
    {
        var rows = await db.ScopedRoleAssignmentSyncOutbox
            .AsNoTracking()
            .Where(r => r.SequenceNumber > afterSequence)
            .OrderBy(r => r.SequenceNumber)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (rows.Count == 0) return [];

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var organizationIds = rows.Select(r => r.OrganizationId).Distinct().ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ExternalObjectId, u.OrganizationId })
            .ToDictionaryAsync(u => u.Id, ct)
            .ConfigureAwait(false);

        var assignments = await db.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId) && ur.OrganizationId != null && organizationIds.Contains(ur.OrganizationId.Value))
            .Select(ur => new
            {
                ur.UserId,
                OrganizationId = ur.OrganizationId!.Value,
                RoleName = ur.Role.Name,
                PermissionCodes = ur.Role.RolePermissions.Select(rp => rp.Permission.Code).ToList(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var held = assignments
            .Where(a => users.TryGetValue(a.UserId, out var user) && user.OrganizationId != a.OrganizationId)
            .ToLookup(a => (a.UserId, a.OrganizationId));

        return [.. rows.Select(row =>
        {
            var pair = held[(row.UserId, row.OrganizationId)].ToList();
            return new ScopedRoleAssignmentSyncEntry
            {
                SequenceNumber = row.SequenceNumber,
                UserId = row.UserId,
                ExternalObjectId = users.TryGetValue(row.UserId, out var user) ? user.ExternalObjectId : string.Empty,
                ResourceOrganizationId = row.OrganizationId,
                RoleNames = [.. pair.Select(a => a.RoleName).Distinct(StringComparer.Ordinal)],
                PermissionCodes = [.. pair.SelectMany(a => a.PermissionCodes).Distinct(StringComparer.Ordinal)],
                IsActive = pair.Count > 0,
            };
        })];
    }
}
