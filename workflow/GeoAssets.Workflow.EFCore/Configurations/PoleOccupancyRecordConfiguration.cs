using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class PoleOccupancyRecordConfiguration : IEntityTypeConfiguration<PoleOccupancyRecord>
{
    internal const string TableName = "field_pole_occupancies";

    public void Configure(EntityTypeBuilder<PoleOccupancyRecord> b)
    {
        b.ToTable(TableName);
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.PoleFeatureId).IsRequired().HasMaxLength(PoleOccupancyEntry.MaxFeatureIdLength);
        b.Property(p => p.OrganizationId).IsRequired();
        // App-managed (see PoleOccupancyRecord.Version): neither IsRowVersion() nor
        // IsConcurrencyToken(). The repository's write is a compare-and-swap on this column.
        b.Property(p => p.Version).IsRequired().ValueGeneratedNever();
        b.Property(p => p.UpdatedAt).IsRequired();

        // One occupancy per pole per owner; also the conflict target of the repository's
        // insert-if-absent, which is what makes two first writers of a pole exclude each other.
        b.HasIndex(p => new { p.OrganizationId, p.PoleFeatureId }).IsUnique();

        b.HasIndex(p => p.PoleFeatureId);
    }

    /// <summary>
    /// PostgreSQL only: gives the pole feature id the byte-wise <c>"C"</c> collation. The reader
    /// contract promises ordinal ordering on every adapter, but a PostgreSQL database's default
    /// collation is linguistic (<c>en_US</c> sorts "a" before "B"; SQLite's BINARY and .NET's
    /// ordinal comparison sort "B" first).
    /// </summary>
    internal static void UseOrdinalTextOrder(EntityTypeBuilder<PoleOccupancyRecord> b) =>
        b.Property(p => p.PoleFeatureId).UseCollation("C");
}
