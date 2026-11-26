using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Selects and orders the layers of each pass. Both passes read only layers a profile defines, never
/// <see cref="LayerVerdict.Unusable"/> ones, and <see cref="LayerVerdict.GeometryOnly"/> ones only when the request
/// includes them. Pass 1 reads every region and layer that has index keys or is a snap target; pass 2 the requested
/// regions and layers. Order: <see cref="LayerDefinition.ProcessingOrder"/>, then region, then layer name.
/// </summary>
internal static class LayerPlanner
{
    public static IReadOnlyList<PlannedLayer> IndexLayers(ImportRequest request) =>
        Plan(request, static (definition, _) => definition.IndexKeys.Count > 0 || definition.SnapTarget);

    public static IReadOnlyList<PlannedLayer> ImportLayers(ImportRequest request) =>
        Plan(request, (_, fileSet) => IsSelected(request.Regions, fileSet.Region) && IsSelected(request.Layers, fileSet.Layer));

    public static string ScopeOf(string scopePrefix, LayerFileSet fileSet) =>
        string.IsNullOrEmpty(fileSet.Region)
            ? string.Concat(scopePrefix, "/", fileSet.Layer)
            : string.Concat(scopePrefix, "/", fileSet.Region, "/", fileSet.Layer);

    private static List<PlannedLayer> Plan(ImportRequest request, Func<LayerDefinition, LayerFileSet, bool> include)
    {
        var planned = new List<PlannedLayer>();
        foreach (var fileSet in request.Inventory.Layers)
        {
            var definition = FindDefinition(request.Profile, fileSet.Layer);
            if (definition is not null && IsReadable(fileSet.Verdict, request.IncludeGeometryOnly) && include(definition, fileSet))
            {
                planned.Add(new PlannedLayer(fileSet, definition, ScopeOf(request.ScopePrefix, fileSet)));
            }
        }

        return
        [
            .. planned
                .OrderBy(static layer => layer.Definition.ProcessingOrder)
                .ThenBy(static layer => layer.FileSet.Region, StringComparer.Ordinal)
                .ThenBy(static layer => layer.FileSet.Layer, StringComparer.Ordinal),
        ];
    }

    private static LayerDefinition? FindDefinition(ISourceProfile profile, string layer)
    {
        foreach (var definition in profile.Layers)
        {
            if (string.Equals(definition.Name, layer, StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    private static bool IsReadable(LayerVerdict verdict, bool includeGeometryOnly) =>
        verdict == LayerVerdict.Usable || (includeGeometryOnly && verdict == LayerVerdict.GeometryOnly);

    private static bool IsSelected(IReadOnlyList<string> selection, string value)
    {
        if (selection.Count == 0)
        {
            return true;
        }

        foreach (var selected in selection)
        {
            if (string.Equals(selected, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
