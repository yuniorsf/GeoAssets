using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class LayerRowConfiguration : IEntityTypeConfiguration<LayerRow>
{
    public void Configure(EntityTypeBuilder<LayerRow> e)
    {
        e.ToTable("layer");
        e.HasKey(x => x.Id);

        e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        e.Property(x => x.Color).HasMaxLength(32).HasDefaultValue("#3388ff");
        e.Property(x => x.IconUrl).HasMaxLength(512).HasDefaultValue(string.Empty);
        e.Property(x => x.DashArray).HasMaxLength(64);
        e.Property(x => x.FillColor).HasMaxLength(32).HasDefaultValue("#3388ff");

        // ── Domain asset type default-layer seed data ───────────────────────────
        // IsBuiltIn = true (shipped by default) but IsProtected = false — a starting catalog,
        // not a hard requirement.
        e.HasData(
            new LayerRow { Id = DomainLayerIds.Pole,               Name = "Poste",                       GeometryType = GeometryType.Point,      Color = "#8b5a2b", Radius = 6 },
            new LayerRow { Id = DomainLayerIds.Transformer,        Name = "Transformador",                GeometryType = GeometryType.Point,      Color = "#e67e22", Radius = 8 },
            new LayerRow { Id = DomainLayerIds.LowTensionWire,     Name = "Línea de baja tensión",        GeometryType = GeometryType.LineString, Color = "#f1c40f", Weight = 2 },
            new LayerRow { Id = DomainLayerIds.WaterDischargePoint, Name = "Punto de descarga de agua",   GeometryType = GeometryType.Point,      Color = "#3498db", Radius = 6 },
            new LayerRow { Id = DomainLayerIds.Breaker,            Name = "Interruptor",                  GeometryType = GeometryType.Point,      Color = "#e74c3c", Radius = 7 }
        );
    }
}
