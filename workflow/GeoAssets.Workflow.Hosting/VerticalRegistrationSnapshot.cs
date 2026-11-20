namespace GeoAssets.Workflow.Hosting;

/// <summary>One reading of <see cref="VerticalRegistrationState"/>.</summary>
/// <param name="Status">Where the registration stands.</param>
/// <param name="ConsecutiveFailures">Failed attempts since the last success.</param>
/// <param name="LastError">Why the last attempt failed; null after a success.</param>
/// <param name="RegisteredAt">When the registration last succeeded; null before the first success.</param>
public sealed record VerticalRegistrationSnapshot(VerticalRegistrationStatus Status, int ConsecutiveFailures, string? LastError, DateTimeOffset? RegisteredAt);
