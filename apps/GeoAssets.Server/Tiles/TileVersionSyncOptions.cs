namespace GeoAssets.Server.Tiles;

/// <summary>
/// How <see cref="TileVersionListener"/> keeps the shared tile versions subscribed (XD01-458): the delay before
/// subscribing again after a lost subscription (<see cref="ReconnectDelay"/>, doubling up to <see cref="MaxReconnectDelay"/>)
/// and how often it retries flushing every replica for a change the store refused (<see cref="RecoveryInterval"/>).
/// </summary>
public sealed record TileVersionSyncOptions
{
    /// <summary>The defaults: 1 s, doubling up to 30 s; a recovery attempt every 5 s.</summary>
    public static TileVersionSyncOptions Default { get; } = new();

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RecoveryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Throws unless every delay is positive and <see cref="MaxReconnectDelay"/> is at least <see cref="ReconnectDelay"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A delay is not positive, or the maximum is below the first.</exception>
    public TileVersionSyncOptions Validated()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RecoveryInterval, TimeSpan.Zero, nameof(RecoveryInterval));
        return this;
    }
}
