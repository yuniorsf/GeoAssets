using GeoAssets.Projects.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Projects.Persistence.Configurations;

internal sealed class ProjectRowConfiguration : IEntityTypeConfiguration<ProjectRow>
{
    public void Configure(EntityTypeBuilder<ProjectRow> b)
    {
        b.ToTable("Projects");
        b.HasKey(p => p.Id);

        b.Property(p => p.Name).IsRequired().HasMaxLength(256);
        b.Property(p => p.Description).HasMaxLength(4096);
        b.Property(p => p.OrganizationId).IsRequired().HasDefaultValue(Guid.Empty);
        b.Property(p => p.CreatedByUserId).IsRequired().HasDefaultValue(Guid.Empty);
        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.UpdatedAt).IsRequired();
        b.Property(p => p.SchemaVersion).IsRequired();
        b.Property(p => p.Kind).IsRequired();

        // No HasColumnType/HasMaxLength on the JSON columns — an unbounded string already maps
        // to each provider's own "no length limit" text type by convention (see
        // ServiceOrderRecordConfiguration for the same reasoning).
        b.Property(p => p.ProvidersJson);
        b.Property(p => p.AssetTypeScopeJson);
        b.Property(p => p.LayerScopeJson);
        b.Property(p => p.ViewStateJson);

        b.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);

        // Global soft-delete filter — every normal read transparently excludes deleted rows.
        // New convention for this codebase (XD01-139); see ProjectRow's doc comment.
        b.HasQueryFilter(p => !p.IsDeleted);

        // Self-referencing fork relationship — a User Project references its General parent.
        // Restrict (not Cascade): deleting a General Project must not silently cascade-delete
        // every User Project forked from it.
        b.HasOne<ProjectRow>()
         .WithMany()
         .HasForeignKey(p => p.ParentProjectId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(p => p.OrganizationId);
        b.HasIndex(p => p.ParentProjectId);
        b.HasIndex(p => p.Kind);
        b.HasIndex(p => p.IsDeleted);
    }
}
