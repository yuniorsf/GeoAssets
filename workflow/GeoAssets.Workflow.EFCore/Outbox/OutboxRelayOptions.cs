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

    /// <summary>
    /// The message types this relay claims (ordinal); empty — the default — claims every type.
    /// A host that delivers only some types itself (XD01-337: <c>GeoAssets.Server</c> applying
    /// asset mutation batches in-process when it has no broker) lists them here, so every other
    /// staged message stays pending instead of being claimed by a relay that cannot deliver it.
    /// </summary>
    public IList<string> MessageTypes { get; } = [];
}
