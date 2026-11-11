using GeoAssets.Identity.Authorization.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class OrganizationGrantSyncOutboxRecordConfiguration : IEntityTypeConfiguration<OrganizationGrantSyncOutboxRecord>
{
    public void Configure(EntityTypeBuilder<OrganizationGrantSyncOutboxRecord> b)
    {
        b.ToTable("OrganizationGrantSyncOutbox");
        b.HasKey(r => r.SequenceNumber);
        b.Property(r => r.SequenceNumber).ValueGeneratedOnAdd();

        b.Property(r => r.ResourceType).HasMaxLength(128);
        b.Property(r => r.ScopeId).HasMaxLength(128);

        b.HasIndex(r => r.GrantId);
    }
}
