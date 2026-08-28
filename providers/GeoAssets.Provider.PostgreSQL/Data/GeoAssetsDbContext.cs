using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
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
    public DbSet<AssetTypeRow> AssetTypes  => Set<AssetTypeRow>();
    public DbSet<LayerRow> Layers => Set<LayerRow>();
    public DbSet<LayerRuleRow> LayerRules => Set<LayerRuleRow>();
    public DbSet<LayerRuleConditionRow> LayerRuleConditions => Set<LayerRuleConditionRow>();

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
            e.Property(x => x.OrganizationId).HasDefaultValue(Guid.Empty);

            e.HasOne(x => x.Layer).WithMany().HasForeignKey(x => x.LayerId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasIndex(x => x.OrganizationId);

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
            e.Property(x => x.IsProtected).HasDefaultValue(false);

            e.Property(x => x.AttributesSchemaJson)
             .HasColumnType("jsonb")
             .HasColumnName("attributes_schema");

            e.Property(x => x.OrganizationId).HasDefaultValue(Guid.Empty);

            e.HasOne(x => x.DefaultLayer).WithMany().HasForeignKey(x => x.DefaultLayerId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => x.DefaultLayerId);
        });

        // ── layer ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<LayerRow>(e =>
        {
            e.ToTable("layer");
            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Color).HasMaxLength(32).HasDefaultValue("#3388ff");
            e.Property(x => x.IconUrl).HasMaxLength(512).HasDefaultValue(string.Empty);
            e.Property(x => x.DashArray).HasMaxLength(64);
            e.Property(x => x.FillColor).HasMaxLength(32).HasDefaultValue("#3388ff");
        });

        // ── layer_rule ──────────────────────────────────────────────────────────
        modelBuilder.Entity<LayerRuleRow>(e =>
        {
            e.ToTable("layer_rule");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.AssetType).WithMany().HasForeignKey(x => x.AssetTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Layer).WithMany().HasForeignKey(x => x.LayerId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.AssetTypeId);
            e.HasIndex(x => x.LayerId);
        });

        // ── layer_rule_condition ────────────────────────────────────────────────
        modelBuilder.Entity<LayerRuleConditionRow>(e =>
        {
            e.ToTable("layer_rule_condition");
            e.HasKey(x => x.Id);

            e.Property(x => x.Attribute).HasMaxLength(128).IsRequired();
            e.Property(x => x.Value).HasMaxLength(512).IsRequired();

            e.HasOne(x => x.LayerRule).WithMany(x => x.Conditions).HasForeignKey(x => x.LayerRuleId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.LayerRuleId);
        });

        // ── Built-in asset type seed data ───────────────────────────────────────
        modelBuilder.Entity<AssetTypeRow>().HasData(
            new AssetTypeRow { Id = AssetType.Point.Id, Name = AssetType.Point.Name, Color = AssetType.Point.Color, IsBuiltIn = true, IsProtected = true },
            new AssetTypeRow { Id = AssetType.Line.Id,  Name = AssetType.Line.Name,  Color = AssetType.Line.Color,  IsBuiltIn = true, IsProtected = true },
            new AssetTypeRow { Id = AssetType.Area.Id,  Name = AssetType.Area.Name,  Color = AssetType.Area.Color,  IsBuiltIn = true, IsProtected = true }
        );

        // ── Domain asset type + default layer seed data ─────────────────────────
        // IsBuiltIn = true (shipped by default) but IsProtected = false (unlike the 3 generic
        // defaults above, these are deletable — they're a starting catalog, not a hard requirement).
        modelBuilder.Entity<LayerRow>().HasData(
            new LayerRow { Id = DomainLayerIds.Pole,               Name = "Poste",                       GeometryType = GeometryType.Point,      Color = "#8b5a2b", Radius = 6 },
            new LayerRow { Id = DomainLayerIds.Transformer,        Name = "Transformador",                GeometryType = GeometryType.Point,      Color = "#e67e22", Radius = 8 },
            new LayerRow { Id = DomainLayerIds.LowTensionWire,     Name = "Línea de baja tensión",        GeometryType = GeometryType.LineString, Color = "#f1c40f", Weight = 2 },
            new LayerRow { Id = DomainLayerIds.WaterDischargePoint, Name = "Punto de descarga de agua",   GeometryType = GeometryType.Point,      Color = "#3498db", Radius = 6 },
            new LayerRow { Id = DomainLayerIds.Breaker,            Name = "Interruptor",                  GeometryType = GeometryType.Point,      Color = "#e74c3c", Radius = 7 }
        );

        modelBuilder.Entity<AssetTypeRow>().HasData(
            new AssetTypeRow { Id = DomainAssetTypeIds.Pole,               Name = "Poste",                     AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Pole,               IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.Transformer,        Name = "Transformador",              AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Transformer,        IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.LowTensionWire,     Name = "Línea de baja tensión",      AllowedGeometryType = GeometryType.LineString, DefaultLayerId = DomainLayerIds.LowTensionWire,     IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.WaterDischargePoint, Name = "Punto de descarga de agua", AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.WaterDischargePoint, IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.Breaker,            Name = "Interruptor",                AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Breaker,             IsBuiltIn = true, IsProtected = false }
        );
    }

    /// <summary>Deterministic IDs for the global domain asset type seed catalog (see <see cref="DomainLayerIds"/>).</summary>
    private static class DomainAssetTypeIds
    {
        public static readonly Guid Pole                = Guid.Parse("00000000-0000-0000-0000-000000000004");
        public static readonly Guid Transformer         = Guid.Parse("00000000-0000-0000-0000-000000000005");
        public static readonly Guid LowTensionWire      = Guid.Parse("00000000-0000-0000-0000-000000000006");
        public static readonly Guid WaterDischargePoint = Guid.Parse("00000000-0000-0000-0000-000000000007");
        public static readonly Guid Breaker             = Guid.Parse("00000000-0000-0000-0000-000000000008");
    }

    /// <summary>Deterministic IDs for each domain asset type's default <see cref="LayerRow"/>.</summary>
    private static class DomainLayerIds
    {
        public static readonly Guid Pole                = Guid.Parse("00000000-0000-0000-0001-000000000001");
        public static readonly Guid Transformer         = Guid.Parse("00000000-0000-0000-0001-000000000002");
        public static readonly Guid LowTensionWire      = Guid.Parse("00000000-0000-0000-0001-000000000003");
        public static readonly Guid WaterDischargePoint = Guid.Parse("00000000-0000-0000-0001-000000000004");
        public static readonly Guid Breaker              = Guid.Parse("00000000-0000-0000-0001-000000000005");
    }
}
