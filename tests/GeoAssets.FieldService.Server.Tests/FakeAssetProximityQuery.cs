using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.FieldWork;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// <see cref="IAssetProximityQuery"/> double for host tests (XD01-386): answers from an in-memory
/// <see cref="IAssetProvider"/> exactly as the guard's in-process provider did before the port
/// existed, or throws <see cref="Failure"/> to simulate the Core GeoAssets Server being down.
/// </summary>
internal sealed class FakeAssetProximityQuery(IAssetProvider assets) : IAssetProximityQuery
{
    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters, CancellationToken ct)
    {
        Calls++;
        return Failure is { } failure
            ? Task.FromException<bool>(failure)
            : assets.IsWithinDistanceAsync(featureId, point, toleranceMeters);
    }
}
