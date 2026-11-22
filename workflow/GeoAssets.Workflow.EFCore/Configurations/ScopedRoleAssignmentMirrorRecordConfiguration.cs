using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ScopedRoleAssignmentMirrorRecordConfiguration : IEntityTypeConfiguration<ScopedRoleAssignmentMirrorRecord>
{
    public void Configure(EntityTypeBuilder<ScopedRoleAssignmentMirrorRecord> b)
    {
        b.ToTable("ScopedRoleAssignmentMirror");
        b.HasKey(r => new { r.UserId, r.ResourceOrganizationId });

        b.Property(r => r.ExternalObjectId).HasMaxLength(256);

        // The caller is looked up by its token's oid.
        b.HasIndex(r => r.ExternalObjectId);
    }
}
