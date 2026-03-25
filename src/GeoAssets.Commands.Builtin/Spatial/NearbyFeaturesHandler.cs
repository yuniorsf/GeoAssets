using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Commands.Builtin.Spatial;

/// <summary>
/// Returns all features within a given radius of a center point.
///
/// Parameters:
///   center         (GeoPoint) — origin of the search
///   radiusDegrees  (double)   — search radius in degrees (~111 km per degree)
/// </summary>
[ExportGeoCommand("nearby-features",
    Category    = "Spatial",
    Description = "Features within radiusDegrees of a GeoPoint center.")]
public sealed class NearbyFeaturesHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var center    = (GeoPoint)parameters["center"];
        var radiusDeg = Convert.ToDouble(parameters["radiusDegrees"]);

        var results = context.Repository.GetNearby(center, radiusDeg);
        return Task.FromResult(GeoCommandResult.Ok(results));
    }
}
