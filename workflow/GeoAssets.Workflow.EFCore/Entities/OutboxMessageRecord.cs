namespace GeoAssets.Workflow.Persistence.Entities;

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string PartitionKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public string? CatalogInstanceId { get; set; }
    public string? SourceService { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public string? HeadersJson { get; set; }
    public int Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextRetryAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public string? LastError { get; set; }
}
