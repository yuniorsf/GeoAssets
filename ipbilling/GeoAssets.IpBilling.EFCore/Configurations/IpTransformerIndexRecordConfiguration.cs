using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class IpTransformerIndexRecordConfiguration : IEntityTypeConfiguration<IpTransformerIndexRecord>
{
    public void Configure(EntityTypeBuilder<IpTransformerIndexRecord> b)
    {
        b.ToTable("IpTransformerIndexes");
        b.HasKey(i => i.Id);

        b.Property(i => i.MunicipioIbgeCode).IsRequired().HasMaxLength(7);
        b.Property(i => i.CodigoInstalacao).IsRequired().HasMaxLength(64);
        b.Property(i => i.TransformerFeatureId).IsRequired();

        b.HasIndex(i => new { i.MunicipioIbgeCode, i.CodigoInstalacao }).IsUnique();
    }
}
