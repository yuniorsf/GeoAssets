using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> b)
    {
        b.ToTable("Roles");
        b.HasKey(r => r.Id);

        b.Property(r => r.Name).IsRequired().HasMaxLength(128);
        b.Property(r => r.Description).HasMaxLength(512);

        b.HasIndex(r => r.Name).IsUnique();

        b.HasMany(r => r.UserRoles)
         .WithOne(ur => ur.Role)
         .HasForeignKey(ur => ur.RoleId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(r => r.RolePermissions)
         .WithOne(rp => rp.Role)
         .HasForeignKey(rp => rp.RoleId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
