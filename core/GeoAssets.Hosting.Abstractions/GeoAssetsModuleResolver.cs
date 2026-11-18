namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Picks the enabled modules out of a host's built-ins (XD01-356): exactly the ids listed, in the listed order — so the
/// order is deterministic and owned by configuration. Fails fast, naming the offending id, on a duplicate built-in id,
/// a duplicate enabled id, or an enabled id no built-in provides. Blank enabled entries are ignored.
/// </summary>
public static class GeoAssetsModuleResolver
{
    public static IReadOnlyList<IGeoAssetsModule> Resolve(
        IReadOnlyList<IGeoAssetsModule> builtIns, IReadOnlyList<string> enabledIds)
    {
        ArgumentNullException.ThrowIfNull(builtIns);
        ArgumentNullException.ThrowIfNull(enabledIds);

        var byId = new Dictionary<string, IGeoAssetsModule>(StringComparer.Ordinal);
        foreach (var module in builtIns)
        {
            ArgumentNullException.ThrowIfNull(module);
            if (string.IsNullOrWhiteSpace(module.Id))
            {
                throw new InvalidOperationException(
                    $"GeoAssets module '{module.GetType().FullName}' has an empty Id.");
            }

            if (!byId.TryAdd(module.Id, module))
            {
                throw new InvalidOperationException(
                    $"Duplicate GeoAssets module id '{module.Id}': registered by both '{byId[module.Id].GetType().FullName}' and '{module.GetType().FullName}'.");
            }
        }

        var enabled = new List<IGeoAssetsModule>(enabledIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in enabledIds)
        {
            // A blank entry enables nothing: it is how configuration that can't remove an array element
            // (an environment variable overriding appsettings.json) disables a listed module.
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException(
                    $"Duplicate GeoAssets module id '{id}' in {GeoAssetsModuleHostingExtensions.ModulesSectionKey}.");
            }

            if (!byId.TryGetValue(id, out var module))
            {
                throw new InvalidOperationException(
                    $"Unknown GeoAssets module id '{id}' in {GeoAssetsModuleHostingExtensions.ModulesSectionKey}. Known ids: {string.Join(", ", byId.Keys.Order(StringComparer.Ordinal))}.");
            }

            enabled.Add(module);
        }

        return enabled;
    }
}
