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
/// </summary>
public class GeoAssetsDbContext(DbContextOptions<GeoAssetsDbContext> options) : DbContext(options)
{
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
    }
}
