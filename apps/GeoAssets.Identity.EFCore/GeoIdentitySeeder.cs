using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Modules;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity;

/// <summary>
/// Seeds <see cref="GeoIdentityDbContext"/> with the canonical roles, permissions, and
/// policies for GeoAssets on startup — the server-side, EF Core-backed equivalent of
/// <c>GeoAssets.Web.Services.Identity.IdentitySeeder</c> (which seeds the WASM client's
/// in-memory store instead). Well-known role/org IDs are duplicated from that class
/// intentionally — the two seeders serve independent hosts until the WASM one is
/// demoted/replaced by HTTP-backed repos (XD01-18).
///
/// Call once after <c>host.Build()</c>, mirroring <c>LoadRegistryFromDbAsync</c>:
/// <code>
///   var app = builder.Build();
///   await app.Services.SeedGeoIdentityAsync();
/// </code>
/// </summary>
public static class GeoIdentitySeeder
{
    // ── Well-known IDs (stable across restarts) ───────────────────────────────

    public static readonly Guid DefaultOrgId     = new("00000000-0000-0000-0000-000000000001");

    public static readonly Guid AdminRoleId      = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid SupervisorRoleId = new("10000000-0000-0000-0000-000000000002");
    public static readonly Guid TechnicianRoleId = new("10000000-0000-0000-0000-000000000003");
    public static readonly Guid ReadOnlyRoleId   = new("10000000-0000-0000-0000-000000000004");

    public static Task SeedAsync(GeoIdentityDbContext db, TimeProvider timeProvider, CancellationToken ct = default) =>
        SeedAsync(db, timeProvider, [], ct);

    /// <summary>
    /// Seeds the kernel's organizations, permissions, roles and policies, then every module's permissions and role
    /// bundles (<see cref="IModulePermissionContributor"/>, XD01-377) — idempotent: seeding the same contributors again
    /// changes nothing, and a contributor no longer registered deletes nothing.
    /// </summary>
    public static async Task SeedAsync(
        GeoIdentityDbContext db, TimeProvider timeProvider, IEnumerable<IModulePermissionContributor> contributors, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(contributors);

        await SeedOrganizationsAsync(db, timeProvider, ct).ConfigureAwait(false);
        await SeedPermissionsAsync(db, ct).ConfigureAwait(false);
        await SeedRolesAsync(db, ct).ConfigureAwait(false);
        await SeedPoliciesAsync(db, ct).ConfigureAwait(false);
        foreach (var contributor in contributors)
        {
            var set = new ModulePermissionSet { Permissions = [.. contributor.Permissions], RoleBundles = [.. contributor.RoleBundles] };
            if (set.FindProblem() is { } problem)
                throw new InvalidOperationException($"Module '{contributor.VerticalId}' contributes an invalid permission set: {problem}");
            await ApplyAsync(db, set, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one module's permissions and role bundles and saves (XD01-377) — what an out-of-process vertical's
    /// <c>PUT /api/identity/verticals/{verticalId}/permissions</c> does. Idempotent; deletes nothing.
    /// </summary>
    /// <returns><see langword="true"/> when anything was added.</returns>
    /// <exception cref="ArgumentException">The set is invalid (<see cref="ModulePermissionSet.FindProblem"/>).</exception>
    public static async Task<bool> ApplyModulePermissionsAsync(GeoIdentityDbContext db, ModulePermissionSet set, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(set);
        if (set.FindProblem() is { } problem) throw new ArgumentException(problem, nameof(set));

        await ApplyAsync(db, set, ct).ConfigureAwait(false);
        if (!db.ChangeTracker.HasChanges()) return false;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task ApplyAsync(GeoIdentityDbContext db, ModulePermissionSet set, CancellationToken ct)
    {
        var existingCodes = (await db.Permissions.Select(p => p.Code).ToListAsync(ct).ConfigureAwait(false))
            .Concat(db.ChangeTracker.Entries<AppPermission>().Select(e => e.Entity.Code))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var permission in set.Permissions.Where(permission => existingCodes.Add(permission.Code)))
            db.Permissions.Add(P(permission.Code, permission.Resource, permission.Action, permission.DescriptionKey));

        foreach (var bundle in set.RoleBundles)
            await AddRoleAsync(db, bundle.RoleId, bundle.RoleName, bundle.Description, isBuiltIn: true, ct, [.. bundle.PermissionCodes]).ConfigureAwait(false);
    }

    // ── Organizations ─────────────────────────────────────────────────────────

    private static async Task SeedOrganizationsAsync(GeoIdentityDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == DefaultOrgId, ct).ConfigureAwait(false))
            db.Organizations.Add(new Organization
            {
                Id          = DefaultOrgId,
                Name        = "GeoAssets Default",
                Slug        = "default",
                Description = "Organización predeterminada del sistema.",
                IsActive    = true,
                CreatedAt   = timeProvider.GetUtcNow().UtcDateTime,
            });
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    private static async Task SeedPermissionsAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        var existingCodes = await db.Permissions.Select(p => p.Code).ToListAsync(ct).ConfigureAwait(false);
        foreach (var perm in SeededPermissions())
            if (!existingCodes.Contains(perm.Code, StringComparer.Ordinal))
                db.Permissions.Add(perm);
    }

    // XD01-156: Description stores an i18n key (resolved via IJsonStringLocalizer in the
    // UI, see PermissionList.razor) instead of literal text, so it can render in the
    // signed-in user's culture. Translations live in wwwroot/i18n/{en,es,pt}.json.
    private static AppPermission[] SeededPermissions() =>
        new[]
        {
            P("serviceorders:create",   "serviceorders", "create",   "permissions.serviceorders.create"),
            P("serviceorders:read",     "serviceorders", "read",     "permissions.serviceorders.read"),
            P("serviceorders:assign",   "serviceorders", "assign",   "permissions.serviceorders.assign"),
            P("serviceorders:complete", "serviceorders", "complete", "permissions.serviceorders.complete"),
            P("serviceorders:cancel",   "serviceorders", "cancel",   "permissions.serviceorders.cancel"),
            P("features:read",          "features",      "read",     "permissions.features.read"),
            P("features:edit",          "features",      "edit",     "permissions.features.edit"),
            P("features:delete",        "features",      "delete",   "permissions.features.delete"),
            P("reports:export",         "reports",       "export",   "permissions.reports.export"),
            P("users:manage",           "users",         "manage",   "permissions.users.manage"),

            // XD01-55: identity admin CRUD (XD01-54 Phase 1) — Administrator-only for now.
            P("users:read",             "users",         "read",       "permissions.users.read"),
            P("users:edit",             "users",         "edit",       "permissions.users.edit"),
            P("roles:read",             "roles",         "read",       "permissions.roles.read"),
            P("roles:edit",             "roles",         "edit",       "permissions.roles.edit"),
            P("roles:delete",           "roles",         "delete",     "permissions.roles.delete"),
            P("permissions:read",       "permissions",   "read",       "permissions.permissions.read"),

            // XD01-128: Organizations/Groups admin CRUD — Administrator-only for now, same
            // precedent as the XD01-55 block above.
            P("organizations:read",     "organizations", "read",       "permissions.organizations.read"),
            P("organizations:edit",     "organizations", "edit",       "permissions.organizations.edit"),

            // XD01-327: the platform administrator — may administer the scoped roles of any organization, not only
            // its own. Deliberately in no built-in role: every organization's administrator holds Administrator, so
            // granting it there would let each of them act on every other organization.
            P("organizations:manage-all", "organizations", "manage-all", "permissions.organizations.manageAll"),
            P("groups:read",            "groups",        "read",       "permissions.groups.read"),
            P("groups:edit",            "groups",        "edit",       "permissions.groups.edit"),

            // XD01-141: Project (working-configuration snapshot) authorization. Split per
            // scope rather than one blanket "projects:edit" so a mutation on one scope
            // (e.g. Providers) can copy-on-write fork a General Project independently of the
            // caller's rights over the other scopes.
            P("projects:read",              "projects", "read",              "permissions.projects.read"),
            P("projects:manage-providers",  "projects", "manage-providers",  "permissions.projects.manageProviders"),
            P("projects:manage-asset-types","projects", "manage-asset-types","permissions.projects.manageAssetTypes"),
            P("projects:manage-layers",     "projects", "manage-layers",     "permissions.projects.manageLayers"),
            P("projects:manage-view",       "projects", "manage-view",       "permissions.projects.manageView"),
            P("projects:rename",            "projects", "rename",            "permissions.projects.rename"),
            P("projects:delete",            "projects", "delete",            "permissions.projects.delete"),

            // XD01-369: publishing a vertical's order-type descriptors to the kernel registry — Administrator-only
            // (a GeoAssets.ServiceCaller service principal satisfies every permission policy anyway).
            P("registry:manage",             "registry",   "manage",           "permissions.registry.manage"),
        };

    // ── Roles with permission assignments ────────────────────────────────────

    private static async Task SeedRolesAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        await AddRoleAsync(db, AdminRoleId,      "Administrator",   "Acceso completo al sistema",       isBuiltIn: true, ct,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "features:delete",
            "reports:export", "users:manage",
            "users:read", "users:edit", "roles:read", "roles:edit", "roles:delete", "permissions:read",
            "organizations:read", "organizations:edit", "groups:read", "groups:edit",
            "projects:read", "projects:manage-providers", "projects:manage-asset-types",
            "projects:manage-layers", "projects:manage-view", "projects:rename", "projects:delete",
            "registry:manage")
            .ConfigureAwait(false);

        await AddRoleAsync(db, SupervisorRoleId, "Supervisor",      "Gestión de órdenes y supervisión", isBuiltIn: true, ct,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "reports:export",
            "projects:read", "projects:manage-providers", "projects:manage-asset-types",
            "projects:manage-layers", "projects:manage-view", "projects:rename")
            .ConfigureAwait(false);

        await AddRoleAsync(db, TechnicianRoleId, "FieldTechnician", "Ejecución de órdenes en campo",    isBuiltIn: true, ct,
            "serviceorders:read", "serviceorders:complete",
            "features:read", "features:edit",
            "projects:read")
            .ConfigureAwait(false);

        await AddRoleAsync(db, ReadOnlyRoleId,   "ReadOnly",        "Solo lectura, sin modificaciones", isBuiltIn: true, ct,
            "serviceorders:read", "features:read",
            "projects:read")
            .ConfigureAwait(false);
    }

    // ── Policies ──────────────────────────────────────────────────────────────

    private static async Task SeedPoliciesAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        await AddPolicyAsync(db, "CanCreateServiceOrders",
            "Puede crear nuevas órdenes de servicio",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "serviceorders:create", null))
            .ConfigureAwait(false);

        await AddPolicyAsync(db, "CanAssignOrders",
            "Puede asignar órdenes a técnicos",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "serviceorders:assign", null))
            .ConfigureAwait(false);

        await AddPolicyAsync(db, "CanManageUsers",
            "Puede gestionar usuarios y roles — solo Administradores",
            PolicyOperator.All, ct,
            (RequirementType.Role, "Administrator", null))
            .ConfigureAwait(false);

        await AddPolicyAsync(db, "CanEditFeatures",
            "Puede editar activos GIS",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "features:edit", null))
            .ConfigureAwait(false);

        await AddPolicyAsync(db, "CanExportReports",
            "Puede exportar reportes",
            PolicyOperator.Any, ct,
            (RequirementType.Role, "Administrator", null),
            (RequirementType.Role, "Supervisor",    null))
            .ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppPermission P(string code, string resource, string action, string desc)
        => new() { Code = code, Resource = resource, Action = action, Description = desc };

    private static async Task AddRoleAsync(
        GeoIdentityDbContext db, Guid id, string name, string description, bool isBuiltIn,
        CancellationToken ct, params string[] permissionCodes)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == id, ct).ConfigureAwait(false))
            db.Roles.Add(new AppRole { Id = id, Name = name, Description = description, IsBuiltIn = isBuiltIn });

        // Backfill any grants missing from an already-existing role too (XD01-90), not just
        // ones just created above — a role's intended permission list can grow in a later
        // code change after an environment was already seeded once, and previously that
        // change never reached a role that already existed by this point.
        var grantedPermissionIds = (await db.RolePermissions
            .Where(rp => rp.RoleId == id)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .ToHashSet();

        // Permissions were seeded (and possibly still pending SaveChanges) earlier in the
        // same unit of work — read from the change tracker, not the database, to see them.
        foreach (var code in permissionCodes)
        {
            var perm = db.ChangeTracker.Entries<AppPermission>().Select(e => e.Entity).FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.Ordinal))
                    ?? await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct).ConfigureAwait(false);
            if (perm is not null && !grantedPermissionIds.Contains(perm.Id))
                db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = perm.Id });
        }
    }

    private static async Task AddPolicyAsync(
        GeoIdentityDbContext db, string name, string description, PolicyOperator op, CancellationToken ct,
        params (RequirementType Type, string Value, string? ClaimValue)[] requirements)
    {
        if (await db.Policies.AnyAsync(p => p.Name == name, ct).ConfigureAwait(false)) return;

        db.Policies.Add(new AppPolicy
        {
            Name         = name,
            Description  = description,
            Operator     = op,
            Requirements = requirements.Select(r => new PolicyRequirement
            {
                Type       = r.Type,
                Value      = r.Value,
                ClaimValue = r.ClaimValue
            }).ToList()
        });
    }
}
