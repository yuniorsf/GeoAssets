using GeoAssets.Identity.Authorization.EFCore.Entities;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GeoAssets.Identity.Authorization.EFCore;

/// <summary>
/// Records, in the save that makes it, every change to what a user holds on another organization (XD01-328): one
/// <see cref="ScopedRoleAssignmentSyncOutboxRecord"/> per (user, organization) pair the pending changes may affect. Run by
/// <see cref="GeoIdentityDbContext"/> before every save, so no write path — a repository method, an admin route, a cascade —
/// can change a scoped assignment without the feed hearing of it.
///
/// A pair is affected by:
/// <list type="bullet">
/// <item>a scoped <see cref="UserRole"/> added, removed or changed (its pair, before and after);</item>
/// <item>a <see cref="RolePermission"/> added or removed, or an <see cref="AppRole"/> renamed or deleted — every scoped
/// assignment of that role;</item>
/// <item>an <see cref="Organization"/> deleted (its scoped assignments cascade away) — every scoped assignment on it;</item>
/// <item>an <see cref="AppUser"/> deleted, or whose <see cref="AppUser.ExternalObjectId"/> or home
/// <see cref="AppUser.OrganizationId"/> changed — every scoped assignment of that user.</item>
/// </list>
/// Permission codes themselves are seeded and never edited, so an <see cref="AppPermission"/> change is not tracked.
/// Recording a pair that did not really change is harmless: the feed resolves it to the pair's current state.
/// </summary>
internal static class ScopedRoleAssignmentChangeCapture
{
    public static void Capture(GeoIdentityDbContext db)
    {
        var changes = Collect(db);
        if (changes.NeedsLookup)
            changes.Pairs.UnionWith(LookupQuery(db, changes).AsEnumerable().Select(p => (p.UserId, p.OrganizationId)));
        Record(db, changes.Pairs);
    }

    public static async Task CaptureAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        var changes = Collect(db);
        if (changes.NeedsLookup)
        {
            var found = await LookupQuery(db, changes).ToListAsync(ct).ConfigureAwait(false);
            changes.Pairs.UnionWith(found.Select(p => (p.UserId, p.OrganizationId)));
        }
        Record(db, changes.Pairs);
    }

    private sealed class Changes
    {
        public HashSet<(Guid UserId, Guid OrganizationId)> Pairs { get; } = [];
        public HashSet<Guid> RoleIds { get; } = [];
        public HashSet<Guid> OrganizationIds { get; } = [];
        public HashSet<Guid> UserIds { get; } = [];
        public bool NeedsLookup => RoleIds.Count > 0 || OrganizationIds.Count > 0 || UserIds.Count > 0;
    }

    private sealed record Pair(Guid UserId, Guid OrganizationId);

    private static Changes Collect(GeoIdentityDbContext db)
    {
        var changes = new Changes();

        foreach (var entry in db.ChangeTracker.Entries<UserRole>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Deleted or EntityState.Modified)) continue;
            AddPair(changes, entry.Property(ur => ur.UserId).OriginalValue, entry.Property(ur => ur.OrganizationId).OriginalValue);
            AddPair(changes, entry.Entity.UserId, entry.Entity.OrganizationId);
        }

        foreach (var entry in db.ChangeTracker.Entries<RolePermission>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted)
                changes.RoleIds.Add(entry.Entity.RoleId);
        }

        foreach (var entry in db.ChangeTracker.Entries<AppRole>())
        {
            if (entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && entry.Property(r => r.Name).IsModified))
                changes.RoleIds.Add(entry.Entity.Id);
        }

        foreach (var entry in db.ChangeTracker.Entries<Organization>())
        {
            if (entry.State == EntityState.Deleted)
                changes.OrganizationIds.Add(entry.Entity.Id);
        }

        foreach (var entry in db.ChangeTracker.Entries<AppUser>())
        {
            if (entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && (Changed(entry, u => u.ExternalObjectId) || Changed(entry, u => u.OrganizationId))))
                changes.UserIds.Add(entry.Entity.Id);
        }

        return changes;
    }

    private static void AddPair(Changes changes, Guid userId, Guid? organizationId)
    {
        if (organizationId is { } organization)
            changes.Pairs.Add((userId, organization));
    }

    private static bool Changed<T>(EntityEntry<AppUser> entry, System.Linq.Expressions.Expression<Func<AppUser, T>> property)
    {
        var value = entry.Property(property);
        return value.IsModified && !EqualityComparer<T>.Default.Equals(value.OriginalValue, value.CurrentValue);
    }

    /// <summary>The stored scoped assignments of the changed roles, organizations and users — read before the save, so a
    /// cascade that is about to remove them is still seen.</summary>
    private static IQueryable<Pair> LookupQuery(GeoIdentityDbContext db, Changes changes)
    {
        var roleIds = changes.RoleIds.ToList();
        var organizationIds = changes.OrganizationIds.ToList();
        var userIds = changes.UserIds.ToList();

        return db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.OrganizationId != null
                      && (roleIds.Contains(ur.RoleId) || organizationIds.Contains(ur.OrganizationId.Value) || userIds.Contains(ur.UserId)))
            .Select(ur => new Pair(ur.UserId, ur.OrganizationId!.Value));
    }

    private static void Record(GeoIdentityDbContext db, HashSet<(Guid UserId, Guid OrganizationId)> pairs)
    {
        foreach (var (userId, organizationId) in pairs.OrderBy(p => p.UserId).ThenBy(p => p.OrganizationId))
            db.ScopedRoleAssignmentSyncOutbox.Add(new ScopedRoleAssignmentSyncOutboxRecord { UserId = userId, OrganizationId = organizationId });
    }
}
