using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> b)
    {
        b.ToTable("UserGroups");
        b.HasKey(ug => new { ug.UserId, ug.GroupId });

        b.HasOne(ug => ug.User)
         .WithMany(u => u.UserGroups)
         .HasForeignKey(ug => ug.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(ug => ug.Group)
         .WithMany(g => g.UserGroups)
         .HasForeignKey(ug => ug.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
