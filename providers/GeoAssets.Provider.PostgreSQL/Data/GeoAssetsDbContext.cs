using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>
/// EF Core DbContext for geospatial assets backed by PostgreSQL + PostGIS.
///
/// Registration (host):
/// <code>
///   services.AddDbContext&lt;GeoAssetsDbContext&gt;(o =>
///       o.UseNpgsql(connectionString, x => x.UseNetTopologySuite()));
/// </code>
///
/// Generate migration:
/// <code>
///   dotnet ef migrations add InitialCreate --project src/GeoAssets.Infrastructure.PostgreSQL
/// </code>
///
/// <paramref name="tenantContext"/> is optional (defaults to <see cref="NullTenantContext"/>,
/// i.e. <see cref="Guid.Empty"/>) so every existing call site that constructs this type directly
/// with just <see cref="DbContextOptions{TContext}"/> — <c>PostgresProviderFactory</c>,
/// <c>PostgresAssetProvider</c>, test fixtures — keeps compiling and behaving exactly as before;
/// only a caller that explicitly supplies a real <see cref="ITenantContext"/> gets row-level
/// tenant scoping.
/// </summary>
public class GeoAssetsDbContext(
    DbContextOptions<GeoAssetsDbContext> options,
    ITenantContext? tenantContext = null) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    public DbSet<GeoEntityRow> GeoEntities => Set<GeoEntityRow>();
    public DbSet<GeoEntityAttributeRow> GeoEntityAttributes => Set<GeoEntityAttributeRow>();
    public DbSet<AssetTypeRow> AssetTypes  => Set<AssetTypeRow>();
    public DbSet<LayerRow> Layers => Set<LayerRow>();
    public DbSet<LayerRuleRow> LayerRules => Set<LayerRuleRow>();
    public DbSet<LayerRuleConditionRow> LayerRuleConditions => Set<LayerRuleConditionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // pg_trgm backs the GIN trigram indexes used by GeoEntityRowConfiguration /
        // GeoEntityAttributeRowConfiguration (leading-wildcard ILIKE, as used by GetPageAsync's
        // SearchText, can't use a plain B-tree index).
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoAssetsDbContext).Assembly);

        // Application-layer half of tenant row isolation (the database-layer half is the
        // native Row-Level Security policy added by the AddGeoEntityRowLevelSecurity
        // migration). Compares against OrganizationId's own default — Guid.Empty — so a host
        // that never supplies a real ITenantContext (NullTenantContext) sees exactly the rows
        // it always saw, no behavior change.
        modelBuilder.Entity<GeoEntityRow>()
            .HasQueryFilter(e => e.OrganizationId == _tenantContext.CurrentOrganizationId);
    }
}
