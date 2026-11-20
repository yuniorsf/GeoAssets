namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The bootstrap registration's retry schedule (XD01-382), section <c>VerticalRegistration</c>: a failed attempt is retried
/// after an exponentially growing delay — <see cref="InitialDelay"/>, doubled per consecutive failure up to
/// <see cref="MaxDelay"/>, with jitter — for as long as the host runs; once registered, the registration is republished
/// every <see cref="RefreshInterval"/>, so a kernel that restarted (its descriptor registry is in memory) learns this vertical
/// again without operator action.
/// </summary>
public sealed record VerticalRegistrationOptions
{
    public const string SectionName = "VerticalRegistration";

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(5);
}
