namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>Retry/backoff configuration for <c>TotvsRetryExecutor</c>, same shape as <c>OutboxRelayOptions</c>.</summary>
public sealed class TotvsSyncRetryOptions
{
    public const string SectionName = "WorkflowTotvsSync";

    public TimeSpan BaseBackoffInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;
}
