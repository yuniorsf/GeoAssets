using GeoAssets.IpBilling.GrantSync;

namespace GeoAssets.Server.Tests;

/// <summary>Records the grant changes a município write queued (XD01-395) without a database.</summary>
internal sealed class RecordingMunicipioGrantSyncQueue : IMunicipioGrantSyncQueue
{
    public List<(MunicipioGrantChange Change, string RequestedBy)> Queued { get; } = [];

    public void Enqueue(MunicipioGrantChange change, string requestedBy) => Queued.Add((change, requestedBy));
}
