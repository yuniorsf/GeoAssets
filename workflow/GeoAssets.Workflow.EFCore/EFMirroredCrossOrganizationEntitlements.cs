using System.Text.Json;
using GeoAssets.Workflow.Rules;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// <see cref="IMirroredCrossOrganizationEntitlements"/> over <see cref="ServiceOrderDbContext"/>'s two mirrors. As fresh as
/// the sync workers keep them: <see cref="Outbox.OrganizationGrantSyncOptions.PollInterval"/> is the staleness bound.
/// </summary>
public sealed class EFMirroredCrossOrganizationEntitlements(ServiceOrderDbContext db, TimeProvider timeProvider)
    : IMirroredCrossOrganizationEntitlements
{
    public async Task<MirroredEntitlements> GetAsync(Guid granteeOrganizationId, string externalObjectId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var grants = granteeOrganizationId == Guid.Empty
            ? []
            : await db.OrganizationGrantMirror
                .AsNoTracking()
                .Where(g => g.GranteeOrganizationId == granteeOrganizationId && g.IsActive && (g.ExpiresAt == null || g.ExpiresAt > now))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var scoped = string.IsNullOrEmpty(externalObjectId)
            ? []
            : await db.ScopedRoleAssignmentMirror
                .AsNoTracking()
                .Where(r => r.ExternalObjectId == externalObjectId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

        if (grants.Count == 0 && scoped.Count == 0)
            return MirroredEntitlements.None;

        return new MirroredEntitlements(
            [.. grants.Select(g => new WorkflowOrgGrant(
                ResourceOrganizationId: g.ResourceOrganizationId.ToString(),
                ResourceType: g.ResourceType,
                AllowedActions: ReadList(g.AllowedActionsJson),
                RequiredRole: g.RequiredRole,
                ScopeId: g.ScopeId)
            {
                ExpiresAt = g.ExpiresAt,
                RequiresScopedRole = g.RequiresScopedRole,
                OrderTypeIds = ReadList(g.OrderTypeIdsJson),
            })],
            ByOrganization(scoped, r => r.RoleNamesJson),
            ByOrganization(scoped, r => r.PermissionCodesJson));
    }

    private static Dictionary<Guid, IReadOnlyList<string>> ByOrganization(
        List<Entities.ScopedRoleAssignmentMirrorRecord> rows, Func<Entities.ScopedRoleAssignmentMirrorRecord, string> json) =>
        rows.GroupBy(r => r.ResourceOrganizationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.SelectMany(r => ReadList(json(r))).Distinct(StringComparer.Ordinal)]);

    private static List<string> ReadList(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
