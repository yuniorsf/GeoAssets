using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// MEF composition root for feature selection strategies.
///
/// Discovers strategies from:
///   • built-in assemblies supplied at construction time
///   • external plugins in <paramref name="pluginsDirectory"/> (GeoAssets.Plugin.*.dll)
///
/// Usage:
/// <code>
///   using var registry = new FeatureSelectionRegistry(
///       pluginsDirectory: Path.Combine(AppContext.BaseDirectory, "plugins"),
///       builtInAssemblies: typeof(BoundingBoxSelectionStrategy).Assembly);
///
///   var features = await registry.SelectAsync("bounding-box", context);
/// </code>
/// </summary>
public sealed class FeatureSelectionRegistry : IDisposable
{
    private readonly CompositionContainer _container;

    [ImportMany]
    public IEnumerable<Lazy<IFeatureSelectionStrategy, IFeatureSelectionStrategyMetadata>> Strategies
    { get; set; } = [];

    public FeatureSelectionRegistry(string pluginsDirectory, params Assembly[] builtInAssemblies)
    {
        var catalog = new AggregateCatalog();

        foreach (var asm in builtInAssemblies)
            catalog.Catalogs.Add(new AssemblyCatalog(asm));

        if (Directory.Exists(pluginsDirectory))
            catalog.Catalogs.Add(new DirectoryCatalog(pluginsDirectory, "GeoAssets.Plugin.*.dll"));

        _container = new CompositionContainer(catalog);
        _container.ComposeParts(this);
    }

    /// <summary>Returns metadata for all registered strategies, ordered by category then name.</summary>
    public IEnumerable<IFeatureSelectionStrategyMetadata> GetAvailableStrategies()
        => Strategies
            .Select(s => s.Metadata)
            .OrderBy(m => m.Category)
            .ThenBy(m => m.DisplayName);

    /// <summary>
    /// Executes the named strategy and returns the resolved feature set.
    /// Also builds and returns a <see cref="FeatureSelectionSpec"/> for audit storage.
    /// </summary>
    public async Task<(IReadOnlyList<GeoFeature> Features, FeatureSelectionSpec Spec)> SelectAsync(
        string strategyId,
        IFeatureSelectionContext context,
        string? note = null,
        CancellationToken ct = default)
    {
        var export = Strategies.FirstOrDefault(
            s => s.Metadata.StrategyId.Equals(strategyId, StringComparison.OrdinalIgnoreCase));

        if (export is null)
            throw new KeyNotFoundException(
                $"Selection strategy '{strategyId}' not found. " +
                $"Available: {string.Join(", ", GetAvailableStrategies().Select(m => m.StrategyId))}");

        var features = await export.Value.SelectAsync(context, ct);

        var spec = new FeatureSelectionSpec
        {
            StrategyId  = strategyId,
            Parameters  = context.Parameters,
            ExecutedAt  = DateTime.UtcNow,
            Note        = note
        };

        return (features, spec);
    }

    public void Dispose() => _container.Dispose();
}
