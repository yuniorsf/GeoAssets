using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);

        b.Property(u => u.AzureObjectId).IsRequired().HasMaxLength(36);
        b.Property(u => u.Email).IsRequired().HasMaxLength(256);
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(256);
        b.Property(u => u.CreatedAt).IsRequired();

        b.HasIndex(u => u.AzureObjectId).IsUnique();
        b.HasIndex(u => u.Email).IsUnique();
        b.HasIndex(u => u.OrganizationId);

        b.HasMany(u => u.UserRoles)
         .WithOne(ur => ur.User)
         .HasForeignKey(ur => ur.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(u => u.UserClaims)
         .WithOne(uc => uc.User)
         .HasForeignKey(uc => uc.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(u => u.UserGroups)
         .WithOne(ug => ug.User)
         .HasForeignKey(ug => ug.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
