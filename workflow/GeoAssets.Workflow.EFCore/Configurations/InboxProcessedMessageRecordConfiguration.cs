using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class InboxProcessedMessageRecordConfiguration : IEntityTypeConfiguration<InboxProcessedMessageRecord>
{
    public void Configure(EntityTypeBuilder<InboxProcessedMessageRecord> b)
    {
        b.ToTable("WorkflowInboxProcessedMessages");
        b.HasKey(x => new { x.MessageId, x.ConsumerName });

        b.Property(x => x.ConsumerName).IsRequired().HasMaxLength(128);
        b.Property(x => x.MessageType).IsRequired().HasMaxLength(256);
        b.Property(x => x.ProcessedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();

        b.HasIndex(x => x.ExpiresAtUtc);
    }
}
