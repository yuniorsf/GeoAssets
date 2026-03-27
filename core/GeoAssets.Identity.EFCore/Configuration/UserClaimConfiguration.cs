using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<UserClaim>
{
    public void Configure(EntityTypeBuilder<UserClaim> b)
    {
        b.ToTable("UserClaims");
        b.HasKey(c => c.Id);

        b.Property(c => c.Type).IsRequired().HasMaxLength(128);
        b.Property(c => c.Value).IsRequired().HasMaxLength(256);

        b.HasIndex(c => new { c.UserId, c.Type });
    }
}
