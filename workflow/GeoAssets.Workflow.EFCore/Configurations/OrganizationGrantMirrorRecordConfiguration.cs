using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrganizationGrantMirrorRecordConfiguration : IEntityTypeConfiguration<OrganizationGrantMirrorRecord>
{
    public void Configure(EntityTypeBuilder<OrganizationGrantMirrorRecord> b)
    {
        b.ToTable("OrganizationGrantMirror");
        b.HasKey(r => r.GrantId);

        b.Property(r => r.ResourceType).HasMaxLength(128);
        b.Property(r => r.ScopeId).HasMaxLength(128);
        b.Property(r => r.RequiredRole).HasMaxLength(128);
        b.Property(r => r.AllowedActionsJson).HasDefaultValue("[]");
        b.Property(r => r.OrderTypeIdsJson).HasDefaultValue("[]");

        // Same shape as the real OrganizationGrants table's own index — the RLS policy's EXISTS
        // subquery filters on exactly these two columns.
        b.HasIndex(r => new { r.GranteeOrganizationId, r.ResourceOrganizationId });
    }
}
