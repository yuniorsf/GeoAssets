using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class PendingInvitationConfiguration : IEntityTypeConfiguration<PendingInvitation>
{
    public void Configure(EntityTypeBuilder<PendingInvitation> b)
    {
        b.ToTable("PendingInvitations");
        b.HasKey(i => i.Id);

        b.Property(i => i.Email).IsRequired().HasMaxLength(256);
        b.Property(i => i.ExternalObjectId).IsRequired().HasMaxLength(36);
        b.Property(i => i.InvitedAt).IsRequired();

        b.HasIndex(i => i.ExternalObjectId).IsUnique();
        b.HasIndex(i => i.Status);
    }
}
