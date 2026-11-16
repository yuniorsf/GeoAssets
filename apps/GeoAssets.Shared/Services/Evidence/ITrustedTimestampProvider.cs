namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// XD01-235's NTP-timestamp binding: a capture-time clock the evidence pipeline doesn't have to
/// trust the capturing device's own clock for, since that clock can be changed by whoever is
/// holding the device. Implementations should fail open to the local system clock rather than
/// block capture when a trusted time source is unreachable — GPS accuracy is this pipeline's hard
/// gate (<see cref="GpsAccuracyGate"/>), not network/NTP reachability.
/// </summary>
public interface ITrustedTimestampProvider
{
    Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default);
}
