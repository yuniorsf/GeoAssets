using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class AssetTypeRowConfiguration : IEntityTypeConfiguration<AssetTypeRow>
{
    public void Configure(EntityTypeBuilder<AssetTypeRow> e)
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

        // ── Built-in asset type seed data ───────────────────────────────────────
        e.HasData(
            new AssetTypeRow { Id = AssetType.Point.Id, Name = AssetType.Point.Name, Color = AssetType.Point.Color, IsBuiltIn = true, IsProtected = true },
            new AssetTypeRow { Id = AssetType.Line.Id,  Name = AssetType.Line.Name,  Color = AssetType.Line.Color,  IsBuiltIn = true, IsProtected = true },
            new AssetTypeRow { Id = AssetType.Area.Id,  Name = AssetType.Area.Name,  Color = AssetType.Area.Color,  IsBuiltIn = true, IsProtected = true }
        );

        // ── Domain asset type seed data ──────────────────────────────────────────
        // IsBuiltIn = true (shipped by default) but IsProtected = false (unlike the 3 generic
        // defaults above, these are deletable — they're a starting catalog, not a hard requirement).
        e.HasData(
            new AssetTypeRow { Id = DomainAssetTypeIds.Pole,               Name = "Poste",                     AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Pole,               IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.Transformer,        Name = "Transformador",              AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Transformer,        IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.LowTensionWire,     Name = "Línea de baja tensión",      AllowedGeometryType = GeometryType.LineString, DefaultLayerId = DomainLayerIds.LowTensionWire,     IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.WaterDischargePoint, Name = "Punto de descarga de agua", AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.WaterDischargePoint, IsBuiltIn = true, IsProtected = false },
            new AssetTypeRow { Id = DomainAssetTypeIds.Breaker,            Name = "Interruptor",                AllowedGeometryType = GeometryType.Point,      DefaultLayerId = DomainLayerIds.Breaker,             IsBuiltIn = true, IsProtected = false }
        );
    }
}
