using GeoAssets.Commands.Contracts;

namespace GeoAssets.Commands.Builtin.Topology;

/// <summary>
/// Dijkstra shortest path (minimum total edge weight) between two features.
///
/// Parameters:
///   fromId  (string) — source feature ID
///   toId    (string) — target feature ID
/// </summary>
[ExportGeoCommand("shortest-path",
    Category    = "Topology",
    Description = "Dijkstra shortest path (min weight) between fromId and toId.")]
public sealed class ShortestPathHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var fromId = (string)parameters["fromId"];
        var toId   = (string)parameters["toId"];

        var path = context.Repository.FindShortestPath(fromId, toId);
        return Task.FromResult(GeoCommandResult.Ok(path));
    }
}
