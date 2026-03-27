using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Web.Services.Identity;

/// <summary>
/// Seeds the <see cref="WasmIdentityStore"/> with the canonical roles, permissions,
/// and policies for GeoAssets on startup.
///
/// Called once from Program.cs before the Blazor host starts:
/// <code>
///   var host = builder.Build();
///   host.Services.GetRequiredService&lt;IdentitySeeder&gt;().Seed();
///   await host.RunAsync();
/// </code>
/// </summary>
public sealed class IdentitySeeder(WasmIdentityStore store)
{
    // ── Well-known IDs (stable across restarts) ───────────────────────────────

    public static readonly Guid DefaultOrgId        = new("00000000-0000-0000-0000-000000000001");

    public static readonly Guid AdminRoleId         = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid SupervisorRoleId    = new("10000000-0000-0000-0000-000000000002");
    public static readonly Guid TechnicianRoleId    = new("10000000-0000-0000-0000-000000000003");
    public static readonly Guid ReadOnlyRoleId      = new("10000000-0000-0000-0000-000000000004");

    public void Seed()
    {
        SeedOrganizations();
        SeedPermissions();
        SeedRoles();
        SeedPolicies();
    }

    // ── Organizations ─────────────────────────────────────────────────────────

    private void SeedOrganizations()
    {
        if (!store.Organizations.Any(o => o.Id == DefaultOrgId))
            store.Organizations.Add(new Organization
            {
                Id          = DefaultOrgId,
                Name        = "GeoAssets Default",
                Slug        = "default",
                Description = "Organización predeterminada del sistema.",
                IsActive    = true,
            });
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    private void SeedPermissions()
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

        foreach (var perm in perms)
            if (!store.Permissions.Any(p => p.Code == perm.Code))
                store.Permissions.Add(perm);
    }

    // ── Roles with permission assignments ────────────────────────────────────

    private void SeedRoles()
    {
        AddRole(AdminRoleId,      "Administrator",    "Acceso completo al sistema",           isBuiltIn: true,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "features:delete",
            "reports:export", "users:manage");

        AddRole(SupervisorRoleId, "Supervisor",       "Gestión de órdenes y supervisión",     isBuiltIn: true,
            "serviceorders:create", "serviceorders:read", "serviceorders:assign",
            "serviceorders:complete", "serviceorders:cancel",
            "features:read", "features:edit", "reports:export");

        AddRole(TechnicianRoleId, "FieldTechnician",  "Ejecución de órdenes en campo",        isBuiltIn: true,
            "serviceorders:read", "serviceorders:complete",
            "features:read", "features:edit");

        AddRole(ReadOnlyRoleId,   "ReadOnly",         "Solo lectura, sin modificaciones",     isBuiltIn: true,
            "serviceorders:read", "features:read");
    }

    // ── Policies ──────────────────────────────────────────────────────────────

    private void SeedPolicies()
    {
        AddPolicy("CanCreateServiceOrders",
            "Puede crear nuevas órdenes de servicio",
            PolicyOperator.All,
            (RequirementType.Permission, "serviceorders:create", null));

        AddPolicy("CanAssignOrders",
            "Puede asignar órdenes a técnicos",
            PolicyOperator.All,
            (RequirementType.Permission, "serviceorders:assign", null));

        AddPolicy("CanManageUsers",
            "Puede gestionar usuarios y roles — solo Administradores",
            PolicyOperator.All,
            (RequirementType.Role, "Administrator", null));

        AddPolicy("CanEditFeatures",
            "Puede editar activos GIS",
            PolicyOperator.All,
            (RequirementType.Permission, "features:edit", null));

        AddPolicy("CanExportReports",
            "Puede exportar reportes",
            PolicyOperator.Any,
            (RequirementType.Role, "Administrator", null),
            (RequirementType.Role, "Supervisor",    null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppPermission P(string code, string resource, string action, string desc)
        => new() { Code = code, Resource = resource, Action = action, Description = desc };

    private void AddRole(Guid id, string name, string description, bool isBuiltIn, params string[] permissionCodes)
    {
        if (store.Roles.Any(r => r.Id == id)) return;

        store.Roles.Add(new AppRole { Id = id, Name = name, Description = description, IsBuiltIn = isBuiltIn });

        foreach (var code in permissionCodes)
        {
            var perm = store.Permissions.FirstOrDefault(p => p.Code == code);
            if (perm is not null && !store.RolePermissions.Any(rp => rp.RoleId == id && rp.PermissionId == perm.Id))
                store.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = perm.Id });
        }
    }

    private void AddPolicy(string name, string description, PolicyOperator op,
        params (RequirementType Type, string Value, string? ClaimValue)[] requirements)
    {
        if (store.Policies.Any(p => p.Name == name)) return;

        store.Policies.Add(new AppPolicy
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
