using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OutboxMessageRecordConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> b)
    {
        b.ToTable("WorkflowOutboxMessages");
        b.HasKey(x => x.Id);

        b.Property(x => x.MessageType).IsRequired().HasMaxLength(256);
        b.Property(x => x.SchemaVersion).IsRequired();
        b.Property(x => x.PartitionKey).IsRequired().HasMaxLength(256);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(128);
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.CatalogInstanceId).HasMaxLength(128);
        b.Property(x => x.SourceService).HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasMaxLength(256);
        b.Property(x => x.CausationId).HasMaxLength(256);
        b.Property(x => x.TraceParent).HasMaxLength(128);
        b.Property(x => x.TraceState).HasMaxLength(512);
        b.Property(x => x.HeadersJson);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.AttemptCount).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.NextRetryAtUtc).IsRequired();
        b.Property(x => x.LockedUntilUtc);
        b.Property(x => x.LockedBy).HasMaxLength(128);
        b.Property(x => x.DispatchedAtUtc);
        b.Property(x => x.LastError).HasMaxLength(2048);

        b.HasIndex(x => new { x.Status, x.NextRetryAtUtc, x.CreatedAtUtc });
        b.HasIndex(x => new { x.Status, x.LockedUntilUtc });
    }
}
