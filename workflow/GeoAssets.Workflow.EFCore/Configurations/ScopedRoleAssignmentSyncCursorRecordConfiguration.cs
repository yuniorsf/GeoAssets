using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ScopedRoleAssignmentSyncCursorRecordConfiguration : IEntityTypeConfiguration<ScopedRoleAssignmentSyncCursorRecord>
{
    public void Configure(EntityTypeBuilder<ScopedRoleAssignmentSyncCursorRecord> b)
    {
        b.ToTable("ScopedRoleAssignmentSyncCursor");
        b.HasKey(r => r.Id);
        // Fixed singleton key (always 1), not database-generated — there is exactly one row.
        b.Property(r => r.Id).ValueGeneratedNever();
    }
}
