namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// A queued grant-sync request the dispatcher claimed (XD01-395): the <see cref="Change"/> to send, who asked for it, and
/// how many attempts already failed.
/// </summary>
public sealed record MunicipioGrantSyncWorkItem(
    long Id,
    MunicipioGrantChange Change,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    int Attempts);
