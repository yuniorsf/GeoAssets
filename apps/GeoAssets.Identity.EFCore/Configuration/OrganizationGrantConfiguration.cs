using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class OrganizationGrantConfiguration : IEntityTypeConfiguration<OrganizationGrant>
{
    public void Configure(EntityTypeBuilder<OrganizationGrant> b)
    {
        b.ToTable("OrganizationGrants");
        b.HasKey(g => g.Id);

        b.Property(g => g.ResourceType).HasMaxLength(128);
        b.Property(g => g.RequiredRole).HasMaxLength(128);
        b.Property(g => g.GrantedBy).IsRequired().HasMaxLength(256);
        b.Property(g => g.GrantedAt).IsRequired();

        // AllowedActions is a flat list of permission codes (no navigation semantics), so it's
        // stored as JSON text rather than a normalized child table — matching this codebase's
        // established convention for this shape of data (e.g. GeoEntityRow.CustomAttributesJson,
        // ServiceOrderRecord.AttributesJson).
        var allowedActionsComparer = new ValueComparer<List<string>>(
            (left, right) => (left ?? new()).SequenceEqual(right ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        b.Property(g => g.AllowedActions)
         .HasConversion(
             v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
             v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
         .Metadata.SetValueComparer(allowedActionsComparer);

        b.HasIndex(g => new { g.GranteeOrganizationId, g.ResourceOrganizationId });
        b.HasIndex(g => g.IsActive);
    }
}
