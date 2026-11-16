using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class PoleOccupancyEntryRecordConfiguration : IEntityTypeConfiguration<PoleOccupancyEntryRecord>
{
    internal const string TableName = "field_pole_occupancy_entries";

    internal const string SlotIndexCheckName = "CK_field_pole_occupancy_entries_SlotIndex";

    /// <summary>
    /// A regulated slot is 1..6 (<c>PoleAttachmentSlotMatrix.MaxSlots</c>); an entry beyond the
    /// regulated slots has none. Double-quoted identifiers, <c>IS NULL</c> and <c>BETWEEN</c>
    /// read the same on PostgreSQL and SQLite.
    /// </summary>
    internal const string SlotIndexCheck = "\"SlotIndex\" IS NULL OR \"SlotIndex\" BETWEEN 1 AND 6";

    internal const string PoleForeignKeyName = "FK_field_pole_occupancy_entries_field_pole_occupancies";

    /// <summary>Index predicate selecting the entries on regulated slots; valid on PostgreSQL and SQLite.</summary>
    internal const string RegulatedSlotFilter = "\"SlotIndex\" IS NOT NULL";

    public void Configure(EntityTypeBuilder<PoleOccupancyEntryRecord> b)
    {
        b.ToTable(TableName, t => t.HasCheckConstraint(SlotIndexCheckName, SlotIndexCheck));
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.PoleOccupancyId).IsRequired();
        b.Property(e => e.Status).IsRequired().HasMaxLength(32);
        b.Property(e => e.OperatorCnpj).HasMaxLength(PoleOccupancyEntry.MaxOperatorCnpjLength);
        b.Property(e => e.AnatelLicenseNumber).HasMaxLength(PoleOccupancyEntry.MaxAnatelLicenseNumberLength);
        b.Property(e => e.CableDiameterMm).IsRequired().HasPrecision(8, PoleOccupancyEntry.MeasurementDecimals);
        b.Property(e => e.CableLoadDaN).IsRequired().HasPrecision(12, PoleOccupancyEntry.MeasurementDecimals);
        b.Property(e => e.AttachedCableFeatureId).HasMaxLength(PoleOccupancyEntry.MaxFeatureIdLength);
        b.Property(e => e.OrganizationId).IsRequired();

        // Composite, on (pole, organization): besides tying an entry to its pole it makes the
        // database itself refuse an entry that carries another organization than its pole's,
        // for every role (row-level security does not apply to the table owner). The index EF
        // adds for it, led by PoleOccupancyId, is also the "all entries of this pole" index —
        // the partial unique index below does not cover entries without a slot.
        b.HasOne<PoleOccupancyRecord>()
         .WithMany()
         .HasForeignKey(e => new { e.PoleOccupancyId, e.OrganizationId })
         .HasPrincipalKey(p => new { p.Id, p.OrganizationId })
         // Named explicitly: the conventional name is longer than PostgreSQL's 63 characters
         // and would be stored truncated.
         .HasConstraintName(PoleForeignKeyName)
         .OnDelete(DeleteBehavior.Cascade);

        // At most one entry per regulated slot of a pole. Partial: entries beyond the regulated
        // slots (no slot index) are outside it, so a pole can hold any number of them.
        b.HasIndex(e => new { e.PoleOccupancyId, e.SlotIndex }).IsUnique().HasFilter(RegulatedSlotFilter);

        // "Which poles is this organization on?"
        b.HasIndex(e => e.OccupantOrganizationId);
        b.HasIndex(e => e.OrganizationId);
    }
}
