namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// Configuration options for <see cref="OutboxRelayProcessor"/>.
/// </summary>
public sealed class OutboxRelayOptions
{
    public const string SectionName = "WorkflowOutbox";

    public string WorkerId { get; set; } = "outbox-worker-default";

    public int BatchSize { get; set; } = 50;

    public TimeSpan BasePollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 5;

    public bool EnableRelayWorker { get; set; } = true;
}
