using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// The outcome of <see cref="BoundedFeatureSelector.SelectAsync"/>: the matched features, distinct
/// and ordered by id (ordinal), and the audit record of the run. <see cref="Spec"/> carries the
/// parameters the strategy actually ran with — the caller's, with the scope overrides applied.
/// </summary>
public sealed record BoundedFeatureSelection(IReadOnlyList<GeoFeature> Features, FeatureSelectionSpec Spec);
