using GeoAssets.Core.Models;
using GeoAssets.Infrastructure.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Infrastructure.PostgreSQL.Data;

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
    public DbSet<AssetTypeRow> AssetTypes  => Set<AssetTypeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── geo_entity ──────────────────────────────────────────────────────────
        modelBuilder.Entity<GeoEntityRow>(e =>
        {
            e.ToTable("geo_entity");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasMaxLength(36);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.AssetTypeId).HasMaxLength(36).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2048).HasDefaultValue(string.Empty);
            e.Property(x => x.LayerId).HasMaxLength(36).HasDefaultValue(string.Empty);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");

            // PostGIS geometry column — no SRID constraint, accepts any CRS.
            // The SRID is stored per-row inside the geometry binary (PostGIS standard).
            // Use ST_SRID(geom) / ST_Transform in queries when CRS conversion is needed.
            e.Property(x => x.Geom)
             .HasColumnType("geometry")
             .HasColumnName("geom");

            // JSONB columns
            e.Property(x => x.CustomAttributesJson)
             .HasColumnType("jsonb")
             .HasColumnName("custom_attributes")
             .HasDefaultValue("{}");

            e.Property(x => x.TopologyJson)
             .HasColumnType("jsonb")
             .HasColumnName("topology")
             .HasDefaultValue("[]");

            e.HasIndex(x => x.AssetTypeId);
            e.HasIndex(x => x.LayerId);

            // Spatial index (GiST) — added via raw SQL in migration
        });

        // ── asset_type ──────────────────────────────────────────────────────────
        modelBuilder.Entity<AssetTypeRow>(e =>
        {
            e.ToTable("asset_type");
            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Color).HasMaxLength(32).HasDefaultValue("#3388ff");
            e.Property(x => x.IconUrl).HasMaxLength(512).HasDefaultValue(string.Empty);
        });

        // ── Built-in asset type seed data ───────────────────────────────────────
        modelBuilder.Entity<AssetTypeRow>().HasData(
            new AssetTypeRow { Id = AssetType.Point.Id, Name = AssetType.Point.Name, Color = AssetType.Point.Color, IsBuiltIn = true },
            new AssetTypeRow { Id = AssetType.Line.Id,  Name = AssetType.Line.Name,  Color = AssetType.Line.Color,  IsBuiltIn = true },
            new AssetTypeRow { Id = AssetType.Area.Id,  Name = AssetType.Area.Name,  Color = AssetType.Area.Color,  IsBuiltIn = true }
        );
    }
}
