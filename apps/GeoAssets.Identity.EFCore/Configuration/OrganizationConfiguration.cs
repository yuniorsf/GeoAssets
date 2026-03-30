using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("Organizations");
        b.HasKey(o => o.Id);

        b.Property(o => o.Name).IsRequired().HasMaxLength(256);
        b.Property(o => o.Slug).IsRequired().HasMaxLength(64);
        b.Property(o => o.Description).HasMaxLength(1024);
        b.Property(o => o.CreatedAt).IsRequired();

        b.HasIndex(o => o.Slug).IsUnique();
        b.HasIndex(o => o.Name);

        b.HasMany(o => o.Users)
         .WithOne(u => u.Organization)
         .HasForeignKey(u => u.OrganizationId)
         .OnDelete(DeleteBehavior.SetNull);
    }
}
