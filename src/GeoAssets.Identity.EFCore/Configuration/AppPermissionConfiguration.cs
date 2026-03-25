using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class AppPermissionConfiguration : IEntityTypeConfiguration<AppPermission>
{
    public void Configure(EntityTypeBuilder<AppPermission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(p => p.Id);

        b.Property(p => p.Code).IsRequired().HasMaxLength(128);
        b.Property(p => p.Resource).IsRequired().HasMaxLength(64);
        b.Property(p => p.Action).IsRequired().HasMaxLength(64);
        b.Property(p => p.Description).HasMaxLength(512);

        b.HasIndex(p => p.Code).IsUnique();

        b.HasMany(p => p.RolePermissions)
         .WithOne(rp => rp.Permission)
         .HasForeignKey(rp => rp.PermissionId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
