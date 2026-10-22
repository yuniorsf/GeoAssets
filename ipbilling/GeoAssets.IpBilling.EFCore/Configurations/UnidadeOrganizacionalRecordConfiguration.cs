using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class UnidadeOrganizacionalRecordConfiguration : IEntityTypeConfiguration<UnidadeOrganizacionalRecord>
{
    public void Configure(EntityTypeBuilder<UnidadeOrganizacionalRecord> b)
    {
        b.ToTable("UnidadesOrganizacionais");
        b.HasKey(u => u.Id);

        b.Property(u => u.Name).IsRequired().HasMaxLength(256);
        b.Property(u => u.Code).HasMaxLength(64);

        b.HasIndex(u => u.MunicipioId);

        // Both tables live in the same IpBillingDbContext, so a real FK is safe here —
        // "not an authorization boundary" (UnidadeOrganizacional's own doc comment) only means
        // no permission/grant check is ever scoped to this row, not that the schema shouldn't
        // enforce referential integrity against its owning Municipio.
        b.HasOne<MunicipioRecord>()
         .WithMany()
         .HasForeignKey(u => u.MunicipioId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
