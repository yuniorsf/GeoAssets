namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>Retry/backoff configuration for <see cref="SiengeRetryExecutor"/>, same shape as <c>OutboxRelayOptions</c>.</summary>
public sealed class SiengeSyncRetryOptions
{
    public const string SectionName = "WorkflowSiengeSync";

    public TimeSpan BaseBackoffInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;
}
