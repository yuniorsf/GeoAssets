using GeoAssets.Commands.Contracts;

namespace GeoAssets.Commands.Builtin.Topology;

/// <summary>
/// Detects whether the topology graph contains at least one cycle.
/// Returns true if cycles exist (network is not a DAG).
///
/// Parameters: none
/// </summary>
[ExportGeoCommand("has-cycles",
    Category    = "Topology",
    Description = "Returns true if the topology graph contains a cycle.")]
public sealed class HasCyclesHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var result = context.Repository.HasCycles();
        return Task.FromResult(GeoCommandResult.Ok(result));
    }
}
