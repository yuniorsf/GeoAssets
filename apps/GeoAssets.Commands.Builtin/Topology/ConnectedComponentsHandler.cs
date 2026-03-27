using GeoAssets.Commands.Contracts;

namespace GeoAssets.Commands.Builtin.Topology;

/// <summary>
/// Groups all features into weakly connected components.
/// Useful for detecting isolated sub-networks.
///
/// Parameters: none
/// </summary>
[ExportGeoCommand("connected-components",
    Category    = "Topology",
    Description = "Groups features into weakly connected components.")]
public sealed class ConnectedComponentsHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var components = context.Repository.GetConnectedComponents();
        return Task.FromResult(GeoCommandResult.Ok(components));
    }
}
