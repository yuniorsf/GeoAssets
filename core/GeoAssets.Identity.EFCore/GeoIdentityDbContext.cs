using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore;

/// <summary>
/// EF Core DbContext for the GeoAssets identity and authorization domain.
///
/// No database provider is configured here — the host registers it:
/// <code>
///   // SQL Server
///   services.AddDbContext&lt;GeoIdentityDbContext&gt;(o =>
///       o.UseSqlServer(configuration.GetConnectionString("Identity")));
///
///   // SQLite (development / MAUI)
///   services.AddDbContext&lt;GeoIdentityDbContext&gt;(o =>
///       o.UseSqlite("Data Source=geoassets-identity.db"));
/// </code>
///
/// Generate the initial migration:
/// <code>
///   dotnet ef migrations add InitialCreate --project src/GeoAssets.Identity
/// </code>
/// </summary>
public class GeoIdentityDbContext(DbContextOptions<GeoIdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization>      Organizations   => Set<Organization>();
    public DbSet<AppGroup>          Groups          => Set<AppGroup>();
    public DbSet<UserGroup>         UserGroups      => Set<UserGroup>();
    public DbSet<AppUser>           Users           => Set<AppUser>();
    public DbSet<AppRole>           Roles           => Set<AppRole>();
    public DbSet<AppPermission>     Permissions     => Set<AppPermission>();
    public DbSet<UserClaim>         UserClaims      => Set<UserClaim>();
    public DbSet<UserRole>          UserRoles       => Set<UserRole>();
    public DbSet<RolePermission>    RolePermissions => Set<RolePermission>();
    public DbSet<AppPolicy>         Policies        => Set<AppPolicy>();
    public DbSet<PolicyRequirement> PolicyRequirements => Set<PolicyRequirement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoIdentityDbContext).Assembly);
    }
}
