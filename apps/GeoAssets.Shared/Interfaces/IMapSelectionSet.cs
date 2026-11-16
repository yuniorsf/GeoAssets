namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// The map's multi-feature selection (XD01-310): a bounded set of feature ids, compared ordinally.
/// Lives alongside <see cref="IFeatureSelectionState"/>, which stays the single "feature open in
/// the properties panel" state — the two are independent.
/// Every mutator ignores blank ids and duplicates, returns what it did, and raises
/// <see cref="Changed"/> exactly once when — and only when — that result is not
/// <see cref="MapSelectionChange.IsEmpty"/>.
/// </summary>
public interface IMapSelectionSet
{
    /// <summary>The selected feature ids.</summary>
    IReadOnlySet<string> Ids { get; }

    /// <summary>The most ids the selection holds; ids beyond it are reported as
    /// <see cref="MapSelectionChange.Overflow"/>, never dropped silently.</summary>
    int MaxCount { get; }

    /// <summary>Raised after the selection changed, or after ids were left out for capacity.</summary>
    event Action<MapSelectionChange>? Changed;

    /// <summary>Adds the ids not yet selected, in order, up to <see cref="MaxCount"/>.</summary>
    MapSelectionChange Add(IReadOnlyCollection<string> ids);

    /// <summary>Removes the ids that are selected.</summary>
    MapSelectionChange Remove(IReadOnlyCollection<string> ids);

    /// <summary>Removes <paramref name="id"/> when selected, otherwise adds it.</summary>
    MapSelectionChange Toggle(string id);

    /// <summary>Makes the selection exactly <paramref name="ids"/> (the first
    /// <see cref="MaxCount"/> distinct ones), reporting only the difference.</summary>
    MapSelectionChange Replace(IReadOnlyCollection<string> ids);

    /// <summary>Empties the selection.</summary>
    MapSelectionChange Clear();
}
