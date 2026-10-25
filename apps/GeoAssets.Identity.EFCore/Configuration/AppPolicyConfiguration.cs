using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class AppPolicyConfiguration : IEntityTypeConfiguration<AppPolicy>
{
    public void Configure(EntityTypeBuilder<AppPolicy> b)
    {
        b.ToTable("Policies");
        b.HasKey(p => p.Id);

        b.Property(p => p.Name).IsRequired().HasMaxLength(128);
        b.Property(p => p.Description).HasMaxLength(512);
        b.Property(p => p.Operator).IsRequired();

        b.HasIndex(p => p.Name).IsUnique();

        b.HasMany(p => p.Requirements)
         .WithOne(r => r.Policy)
         .HasForeignKey(r => r.PolicyId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
