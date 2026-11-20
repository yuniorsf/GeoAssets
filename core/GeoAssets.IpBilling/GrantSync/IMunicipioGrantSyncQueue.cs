using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// The write side of the município grant-sync outbox (XD01-395): stages a <see cref="MunicipioGrantChange"/> in the same
/// unit of work as the município save that called for it, so <see cref="IMunicipioRepository.SaveChangesAsync"/> commits
/// the município and its request together — or neither. Nothing reaches the kernel here: a background dispatcher sends the
/// committed requests, so a kernel outage never fails a município save.
/// </summary>
public interface IMunicipioGrantSyncQueue
{
    /// <summary>Stages <paramref name="change"/>, requested by <paramref name="requestedBy"/> (the caller's user id);
    /// committed by the município repository's next <c>SaveChangesAsync</c>.</summary>
    void Enqueue(MunicipioGrantChange change, string requestedBy);
}
