using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.NeutralOperator.Persistence.Configurations;

internal sealed class OccupancyApplySagaRecordConfiguration : IEntityTypeConfiguration<OccupancyApplySagaRecord>
{
    internal const string TableName = "occupancy_apply_sagas";

    internal const int MaxOrderTypeIdLength = 128;

    internal const int MaxStatusLength = 64;

    public void Configure(EntityTypeBuilder<OccupancyApplySagaRecord> b)
    {
        b.ToTable(TableName);
        b.HasKey(s => s.BatchId);

        b.Property(s => s.BatchId).ValueGeneratedNever();
        b.Property(s => s.ServiceOrderId).IsRequired().HasMaxLength(OccupancyChangeSet.MaxOrderKeyLength);
        b.Property(s => s.OrderTypeId).IsRequired().HasMaxLength(MaxOrderTypeIdLength);
        b.Property(s => s.Attempt).IsRequired();
        b.Property(s => s.PreviousStatus).IsRequired().HasMaxLength(MaxStatusLength);
        b.Property(s => s.ApprovedStatus).IsRequired().HasMaxLength(MaxStatusLength);
        b.Property(s => s.RequestedBy).IsRequired().HasMaxLength(OccupancyChangeSet.MaxStagedByLength);
        b.Property(s => s.RequestedAt).IsRequired();
        b.Property(s => s.LastRequestedAt).IsRequired();
        b.Property(s => s.RetryCount).IsRequired();
        b.Property(s => s.State).IsRequired().HasMaxLength(16);
        b.Property(s => s.ReasonCode).HasMaxLength(OccupancyApplySaga.MaxReasonCodeLength);
        b.Property(s => s.Reason).HasMaxLength(OccupancyApplySaga.MaxReasonLength);
        b.Property(s => s.ConflictingFeatureIdsJson).IsRequired();
        b.Property(s => s.BatchJson).IsRequired();
        b.Property(s => s.OrganizationId).IsRequired();

        // One saga per approval of an order; "the order's latest" is its highest attempt.
        b.HasIndex(s => new { s.ServiceOrderId, s.Attempt }).IsUnique();

        // The timeout sweeper: pending sagas by when they were last staged.
        b.HasIndex(s => new { s.State, s.LastRequestedAt });
        b.HasIndex(s => s.OrganizationId);
    }
}
