using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

/// <summary>
/// Normalized, indexed copy of geo_entity.custom_attributes (jsonb stays the source of truth
/// for everything else) — GetPageAsync's SearchText match against custom attribute keys/values
/// used to run an unconditional jsonb_each_text scan over the whole table on every search; this
/// table lets that match use real trigram indexes instead (XD01-171).
/// </summary>
internal sealed class GeoEntityAttributeRowConfiguration : IEntityTypeConfiguration<GeoEntityAttributeRow>
{
    public void Configure(EntityTypeBuilder<GeoEntityAttributeRow> e)
    {
        e.ToTable("geo_entity_attribute");
        e.HasKey(x => new { x.EntityId, x.Key });

        e.Property(x => x.EntityId).HasMaxLength(36).IsRequired();
        e.Property(x => x.Key).HasMaxLength(128).IsRequired();
        e.Property(x => x.Value).IsRequired();

        e.HasOne(x => x.Entity).WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => x.Key, "IX_geo_entity_attribute_Key_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        e.HasIndex(x => x.Value, "IX_geo_entity_attribute_Value_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
    }
}
