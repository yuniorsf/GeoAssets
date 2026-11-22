using GeoAssets.Identity.Authorization.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class ScopedRoleAssignmentSyncOutboxRecordConfiguration : IEntityTypeConfiguration<ScopedRoleAssignmentSyncOutboxRecord>
{
    public void Configure(EntityTypeBuilder<ScopedRoleAssignmentSyncOutboxRecord> b)
    {
        b.ToTable("ScopedRoleAssignmentSyncOutbox");
        b.HasKey(r => r.SequenceNumber);
        b.Property(r => r.SequenceNumber).ValueGeneratedOnAdd();
    }
}
