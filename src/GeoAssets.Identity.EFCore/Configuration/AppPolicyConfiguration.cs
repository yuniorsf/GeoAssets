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

internal sealed class PolicyRequirementConfiguration : IEntityTypeConfiguration<PolicyRequirement>
{
    public void Configure(EntityTypeBuilder<PolicyRequirement> b)
    {
        b.ToTable("PolicyRequirements");
        b.HasKey(r => r.Id);

        b.Property(r => r.Type).IsRequired();
        b.Property(r => r.Value).IsRequired().HasMaxLength(256);
        b.Property(r => r.ClaimValue).HasMaxLength(256);
    }
}
