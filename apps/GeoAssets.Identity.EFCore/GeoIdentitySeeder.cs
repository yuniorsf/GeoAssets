using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
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

    public static async Task SeedAsync(
        GeoIdentityDbContext db, TimeProvider timeProvider, CancellationToken ct = default)
    {
        await SeedOrganizationsAsync(db, timeProvider, ct);
        await SeedPermissionsAsync(db, ct);
        await SeedRolesAsync(db, ct);
        await SeedPoliciesAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Organizations ─────────────────────────────────────────────────────────

    private static async Task SeedOrganizationsAsync(GeoIdentityDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == DefaultOrgId, ct))
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
        var perms = new[]
        {
            P("serviceorders:create",   "serviceorders", "create",   "Crear nuevas órdenes de servicio"),
            P("serviceorders:read",     "serviceorders", "read",     "Ver órdenes de servicio"),
            P("serviceorders:assign",   "serviceorders", "assign",   "Asignar órdenes a técnicos"),
            P("serviceorders:complete", "serviceorders", "complete", "Marcar órdenes como completadas"),
            P("serviceorders:cancel",   "serviceorders", "cancel",   "Cancelar órdenes de servicio"),
            P("features:read",          "features",      "read",     "Ver activos GIS"),
            P("features:edit",          "features",      "edit",     "Editar activos GIS"),
            P("features:delete",        "features",      "delete",   "Eliminar activos GIS"),
            P("reports:export",         "reports",       "export",   "Exportar reportes"),
            P("users:manage",           "users",         "manage",   "Gestionar usuarios y roles"),
        };

        var existingCodes = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        foreach (var perm in perms)
            if (!existingCodes.Contains(perm.Code))
                db.Permissions.Add(perm);
    }

    // ── Roles with permission assignments ────────────────────────────────────

    private static async Task SeedRolesAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        await AddRoleAsync(db, AdminRoleId,      "Administrator",   "Acceso completo al sistema",       isBuiltIn: true, ct,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "features:delete",
            "reports:export", "users:manage");

        await AddRoleAsync(db, SupervisorRoleId, "Supervisor",      "Gestión de órdenes y supervisión", isBuiltIn: true, ct,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "reports:export");

        await AddRoleAsync(db, TechnicianRoleId, "FieldTechnician", "Ejecución de órdenes en campo",    isBuiltIn: true, ct,
            "serviceorders:read", "serviceorders:complete",
            "features:read", "features:edit");

        await AddRoleAsync(db, ReadOnlyRoleId,   "ReadOnly",        "Solo lectura, sin modificaciones", isBuiltIn: true, ct,
            "serviceorders:read", "features:read");
    }

    // ── Policies ──────────────────────────────────────────────────────────────

    private static async Task SeedPoliciesAsync(GeoIdentityDbContext db, CancellationToken ct)
    {
        await AddPolicyAsync(db, "CanCreateServiceOrders",
            "Puede crear nuevas órdenes de servicio",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "serviceorders:create", null));

        await AddPolicyAsync(db, "CanAssignOrders",
            "Puede asignar órdenes a técnicos",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "serviceorders:assign", null));

        await AddPolicyAsync(db, "CanManageUsers",
            "Puede gestionar usuarios y roles — solo Administradores",
            PolicyOperator.All, ct,
            (RequirementType.Role, "Administrator", null));

        await AddPolicyAsync(db, "CanEditFeatures",
            "Puede editar activos GIS",
            PolicyOperator.All, ct,
            (RequirementType.Permission, "features:edit", null));

        await AddPolicyAsync(db, "CanExportReports",
            "Puede exportar reportes",
            PolicyOperator.Any, ct,
            (RequirementType.Role, "Administrator", null),
            (RequirementType.Role, "Supervisor",    null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppPermission P(string code, string resource, string action, string desc)
        => new() { Code = code, Resource = resource, Action = action, Description = desc };

    private static async Task AddRoleAsync(
        GeoIdentityDbContext db, Guid id, string name, string description, bool isBuiltIn,
        CancellationToken ct, params string[] permissionCodes)
    {
        if (await db.Roles.AnyAsync(r => r.Id == id, ct)) return;

        db.Roles.Add(new AppRole { Id = id, Name = name, Description = description, IsBuiltIn = isBuiltIn });

        // Permissions were seeded (and possibly still pending SaveChanges) earlier in the
        // same unit of work — read from the change tracker, not the database, to see them.
        foreach (var code in permissionCodes)
        {
            var perm = db.ChangeTracker.Entries<AppPermission>().Select(e => e.Entity).FirstOrDefault(p => p.Code == code)
                    ?? await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
            if (perm is not null)
                db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = perm.Id });
        }
    }

    private static async Task AddPolicyAsync(
        GeoIdentityDbContext db, string name, string description, PolicyOperator op, CancellationToken ct,
        params (RequirementType Type, string Value, string? ClaimValue)[] requirements)
    {
        if (await db.Policies.AnyAsync(p => p.Name == name, ct)) return;

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
