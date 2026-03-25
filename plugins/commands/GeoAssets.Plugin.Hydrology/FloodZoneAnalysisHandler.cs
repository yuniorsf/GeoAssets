using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Plugin.Hydrology;

/// <summary>
/// External plugin handler — ships as GeoAssets.Plugin.Hydrology.dll.
///
/// Identifies features that fall within or intersect a flood-zone polygon.
/// Drop the compiled DLL into the host's plugins/ directory; no recompilation needed.
///
/// Parameters:
///   floodZone  (GeoPolygon) — the flood zone boundary
/// </summary>
[ExportGeoCommand("flood-zone-analysis",
    Category    = "Hydrology",
    Description = "Features intersecting a flood zone polygon. External plugin demo.")]
public sealed class FloodZoneAnalysisHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var floodZone = (GeoPolygon)parameters["floodZone"];
        var atRisk    = context.Repository.GetIntersecting(floodZone);
        return Task.FromResult(GeoCommandResult.Ok(atRisk));
    }
}
