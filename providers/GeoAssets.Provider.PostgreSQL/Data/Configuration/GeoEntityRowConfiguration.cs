using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class GeoEntityRowConfiguration : IEntityTypeConfiguration<GeoEntityRow>
{
    public void Configure(EntityTypeBuilder<GeoEntityRow> e)
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

        // GetPageAsync's SearchText path ILIKE-matches Name/Description with a leading
        // wildcard, which a B-tree index can't serve — trigram GIN indexes can (XD01-171).
        // Named explicitly (HasIndex(expr, name) overload) so Name gets this GIN trigram
        // index *and* the separate B-tree index below for sorting — calling HasIndex(x =>
        // x.Name) twice without distinct names reconfigures the same index instead of
        // creating two.
        e.HasIndex(x => x.Name, "IX_geo_entity_Name_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        e.HasIndex(x => x.Description, "IX_geo_entity_Description_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

        // GIN index for future containment/existence (@>, ?) queries on custom_attributes.
        // Note: GetPageAsync's current SearchText match goes through jsonb_each_text + ILIKE
        // on the expanded rows, which this index does NOT accelerate (GIN on jsonb serves
        // containment/key-existence operators, not per-row ILIKE on unnested text) — a query
        // rewrite to @>/?/jsonb_path_ops would be needed for that path, left out of scope here
        // as the ticket itself flagged as a separate, larger effort (XD01-171).
        e.HasIndex(x => x.CustomAttributesJson, "IX_geo_entity_custom_attributes").HasMethod("gin");

        // B-tree indexes for GetPageAsync's non-default SortBy paths ("name"/"createdAt"/
        // "updatedAt") — only the default Id sort was index-backed via the PK (XD01-171).
        e.HasIndex(x => x.Name, "IX_geo_entity_Name");
        e.HasIndex(x => x.CreatedAt);
        e.HasIndex(x => x.UpdatedAt);

        // Spatial index (GiST) — added via raw SQL in migration
    }
}
