namespace GeoAssets.IpBilling.Server;

/// <summary>
/// The município grant-sync dispatcher's settings (XD01-395), section <c>GrantSync</c>, validated on start: every
/// <see cref="PollInterval"/> it claims up to <see cref="BatchSize"/> due requests under a <see cref="LeaseDuration"/> lease;
/// a failed attempt is retried after <see cref="InitialBackoff"/> doubled per failed attempt, capped at
/// <see cref="MaxBackoff"/> and jittered, until <see cref="MaxAttempts"/> attempts have failed — then the request is marked
/// Failed and stays visible. <see cref="Enabled"/> false registers the dispatcher but never runs it.
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

    /// <summary>Whether these settings can run the dispatcher (see <see cref="ValidationMessage"/>).</summary>
    public static bool IsValid(MunicipioGrantSyncOptions options) =>
        options is not null
        && options.PollInterval > TimeSpan.Zero
        && options.BatchSize >= 1
        && options.LeaseDuration > TimeSpan.Zero
        && options.InitialBackoff > TimeSpan.Zero
        && options.MaxBackoff >= options.InitialBackoff
        && options.MaxAttempts >= 1;

    public const string ValidationMessage =
        $"{SectionName}: PollInterval, LeaseDuration and InitialBackoff must be positive, MaxBackoff at least InitialBackoff, BatchSize and MaxAttempts at least 1.";
}
