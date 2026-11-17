using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasKey(ur => ur.Id);

        b.Property(ur => ur.AssignedAt).IsRequired();
        b.Property(ur => ur.AssignedBy).HasMaxLength(256);

        // One assignment per (user, role, scope), where "no organization" (the home scope) is a
        // scope like any other. A plain unique index treats NULLs as distinct, so the home scope
        // gets its own filtered unique index. Two indexes rather than PostgreSQL's
        // NULLS NOT DISTINCT because this project is provider-agnostic (its tests run on SQLite).
        b.HasIndex(ur => new { ur.UserId, ur.RoleId })
         .IsUnique()
         .HasFilter("\"OrganizationId\" IS NULL");
        b.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.OrganizationId })
         .IsUnique();

        b.HasOne(ur => ur.Organization)
         .WithMany()
         .HasForeignKey(ur => ur.OrganizationId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
