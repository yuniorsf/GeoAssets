namespace GeoAssets.Workflow.TestKit;

/// <summary>One typed message as it was handed to <see cref="RecordingOutboxWriter.EnqueueAsync"/>.</summary>
public sealed record StagedOutboxMessage(
    Guid MessageId, object Message, string MessageType, string PartitionKey, string? CorrelationId, string? CausationId);
