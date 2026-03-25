using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Models;

namespace GeoAssets.Commands.Builtin.Asset;

/// <summary>
/// Adds a GeoFeature to the repository.
///
/// Parameters:
///   feature  (GeoFeature) — the feature to add
/// </summary>
[ExportGeoCommand("add-feature",
    Category    = "Asset",
    Description = "Adds a GeoFeature to the repository.")]
public sealed class AddFeatureHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        var feature = (GeoFeature)parameters["feature"];
        context.Repository.Add(feature);
        return Task.FromResult(GeoCommandResult.Ok(feature));
    }
}
