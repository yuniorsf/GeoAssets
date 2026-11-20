namespace GeoAssets.IpBilling.Server;

/// <summary>
/// The município grant-sync dispatcher's settings (XD01-395), section <c>GrantSync</c>, validated on start: every
/// <see cref="PollInterval"/> it claims up to <see cref="BatchSize"/> due requests under a <see cref="LeaseDuration"/> lease;
/// a failed attempt is retried after <see cref="InitialBackoff"/> doubled per failed attempt, capped at
/// <see cref="MaxBackoff"/> and jittered, until <see cref="MaxAttempts"/> attempts have failed — then the request is marked
/// Failed and stays visible. Every <see cref="PruneInterval"/> the pruner (XD01-400) deletes, up to
/// <see cref="PruneBatchSize"/> rows at a time, requests completed more than <see cref="CompletedRetention"/> ago and — only
/// when <see cref="FailedRetention"/> is set — requests failed more than that ago, never a município's latest request.
/// <see cref="Enabled"/> false registers the dispatcher and the pruner but never runs them.
/// </summary>
internal sealed record MunicipioGrantSyncOptions
{
    public const string SectionName = "GrantSync";

    public bool Enabled { get; init; } = true;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; init; } = 20;

    /// <summary>How long a claimed request is held before another worker may take it over (a crashed worker's).</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; init; } = 20;

    /// <summary>How long a completed request is kept before the pruner may delete it.</summary>
    public TimeSpan CompletedRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>How long a failed request is kept before the pruner may delete it; null (the default) keeps them.</summary>
    public TimeSpan? FailedRetention { get; init; }

    public int PruneBatchSize { get; init; } = 500;

    public TimeSpan PruneInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Whether these settings can run the dispatcher (see <see cref="ValidationMessage"/>).</summary>
    public static bool IsValid(MunicipioGrantSyncOptions options) =>
        options is not null
        && options.PollInterval > TimeSpan.Zero
        && options.BatchSize >= 1
        && options.LeaseDuration > TimeSpan.Zero
        && options.InitialBackoff > TimeSpan.Zero
        && options.MaxBackoff >= options.InitialBackoff
        && options.MaxAttempts >= 1
        && options.CompletedRetention > TimeSpan.Zero
        && (options.FailedRetention is null || options.FailedRetention > TimeSpan.Zero)
        && options.PruneBatchSize >= 1
        && options.PruneInterval > TimeSpan.Zero;

    public const string ValidationMessage =
        $"{SectionName}: PollInterval, LeaseDuration, InitialBackoff, CompletedRetention and PruneInterval must be positive, FailedRetention empty or positive, MaxBackoff at least InitialBackoff, BatchSize, MaxAttempts and PruneBatchSize at least 1.";
}
