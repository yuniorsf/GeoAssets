namespace GeoAssets.Workflow.Persistence.Entities;

internal sealed class InboxProcessedMessageRecord
{
    public Guid MessageId { get; set; }
    public string ConsumerName { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
