using System.Globalization;
using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Commands.Builtin.Spatial;

/// <summary>
/// Returns all features that intersect the buffer of a given geometry.
///
/// Parameters:
///   geometry       (GeoGeometry) — source geometry to buffer
///   distanceDegrees (double)     — buffer distance in degrees
/// </summary>
[ExportGeoCommand("buffer-intersects",
    Category    = "Spatial",
    Description = "Features intersecting a buffer expanded by distanceDegrees.")]
public sealed class BufferHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);

        var geometry = (GeoGeometry)parameters["geometry"];
        var distance = Convert.ToDouble(parameters["distanceDegrees"], CultureInfo.InvariantCulture);

        var buffered = geometry.Buffer(distance);
        var results  = context.Repository.GetIntersecting(buffered);
        return Task.FromResult(GeoCommandResult.Ok(results));
    }
}
