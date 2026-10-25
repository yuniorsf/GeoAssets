using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

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
