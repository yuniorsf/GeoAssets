using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class AppGroupConfiguration : IEntityTypeConfiguration<AppGroup>
{
    public void Configure(EntityTypeBuilder<AppGroup> b)
    {
        b.ToTable("Groups");
        b.HasKey(g => g.Id);

        b.Property(g => g.Name).IsRequired().HasMaxLength(256);
        b.Property(g => g.Description).HasMaxLength(1024);
        b.Property(g => g.CreatedAt).IsRequired();

        b.HasIndex(g => g.OrganizationId);
        b.HasIndex(g => new { g.OrganizationId, g.Name }).IsUnique();

        b.HasOne(g => g.Organization)
         .WithMany()
         .HasForeignKey(g => g.OrganizationId)
         .OnDelete(DeleteBehavior.SetNull);

        b.HasMany(g => g.UserGroups)
         .WithOne(ug => ug.Group)
         .HasForeignKey(ug => ug.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
