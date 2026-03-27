using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Base class for strategies that run an automated background process
/// to determine which features belong in a service order.
///
/// Use this when selection logic is too complex for a simple filter and
/// requires computational analysis, external data, or long-running I/O.
///
/// Subclass and override <see cref="RunAsync"/> to implement the process.
/// The base class wraps it with progress reporting and cancellation support.
///
/// Parameters:
///   Any parameters the concrete subclass requires.
/// </summary>
public abstract class BackgroundProcessSelectionStrategy : IFeatureSelectionStrategy
{
    public abstract string StrategyId  { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    /// <summary>
    /// Implement the actual background selection logic here.
    /// Called by <see cref="SelectAsync"/> with a scoped <see cref="IProgress{T}"/> reporter.
    /// </summary>
    protected abstract Task<IReadOnlyList<GeoFeature>> RunAsync(
        IFeatureSelectionContext context,
        IProgress<BackgroundSelectionProgress> progress,
        CancellationToken ct);

    public async Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var progress = new Progress<BackgroundSelectionProgress>(report =>
            OnProgress?.Invoke(this, report));

        return await RunAsync(context, progress, ct);
    }

    /// <summary>Raised when the background process reports incremental progress.</summary>
    public event EventHandler<BackgroundSelectionProgress>? OnProgress;
}

/// <summary>Progress snapshot reported by a background selection process.</summary>
public sealed record BackgroundSelectionProgress(
    int    PercentComplete,
    string Message,
    int    FeaturesFoundSoFar = 0);
