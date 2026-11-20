using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Identity.Authorization.EFCore.Configuration;

internal sealed class OrganizationGrantConfiguration : IEntityTypeConfiguration<OrganizationGrant>
{
    /// <summary>
    /// One active grant per <c>OrganizationGrantKey</c> (XD01-394): unique over the key's four columns among the rows
    /// flagged active, expired or not. On PostgreSQL a null resource type or scope compares equal to another null
    /// (<c>NULLS NOT DISTINCT</c>); other providers (SQLite in tests) treat nulls as distinct, so there the rule only
    /// holds for keys without one.
    /// </summary>
    internal const string ActiveKeyIndexName = "UX_OrganizationGrants_ActiveKey";

    public void Configure(EntityTypeBuilder<OrganizationGrant> b)
    {
        b.ToTable("OrganizationGrants");
        b.HasKey(g => g.Id);

        b.Property(g => g.ResourceType).HasMaxLength(128);
        b.Property(g => g.ScopeId).HasMaxLength(128);
        b.Property(g => g.RequiredRole).HasMaxLength(128);
        b.Property(g => g.GrantedBy).IsRequired().HasMaxLength(256);
        b.Property(g => g.GrantedAt).IsRequired();

        // AllowedActions is a flat list of permission codes (no navigation semantics), so it's
        // stored as JSON text rather than a normalized child table — matching this codebase's
        // established convention for this shape of data (e.g. GeoEntityRow.CustomAttributesJson,
        // ServiceOrderRecord.AttributesJson).
        var allowedActionsComparer = new ValueComparer<List<string>>(
            (left, right) => (left ?? new()).SequenceEqual(right ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode(StringComparison.Ordinal))),
            v => v.ToList());

        b.Property(g => g.AllowedActions)
         .HasConversion(
             v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
             v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
         .Metadata.SetValueComparer(allowedActionsComparer);

        b.HasIndex(g => new { g.GranteeOrganizationId, g.ResourceOrganizationId });
        b.HasIndex(g => g.IsActive);
        b.HasIndex(g => new { g.GranteeOrganizationId, g.ResourceOrganizationId, g.ResourceType, g.ScopeId }, ActiveKeyIndexName)
         .IsUnique()
         .HasFilter("\"IsActive\"")
         .AreNullsDistinct(false);
    }
}
