using System.Text.RegularExpressions;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Matches file stems against a layer: the whole stem against <see cref="LayerDefinition.FileStemPattern"/>
/// (case-insensitive, culture-invariant, with a match timeout) when it is set, else equality with
/// <see cref="LayerDefinition.FileStem"/> (case-insensitive).
/// </summary>
internal static class LayerStemMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>True when <paramref name="stem"/> names a file set of <paramref name="definition"/>.</summary>
    public static bool Matches(LayerDefinition definition, string stem) =>
        HasPattern(definition)
            ? MatchesPattern(definition.FileStemPattern!, stem)
            : string.Equals(stem, definition.FileStem, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="stem"/> equals the layer's stem or matches its pattern (never an orphan table).</summary>
    public static bool IsKnown(LayerDefinition definition, string stem) =>
        string.Equals(stem, definition.FileStem, StringComparison.OrdinalIgnoreCase)
        || (HasPattern(definition) && MatchesPattern(definition.FileStemPattern!, stem));

    private static bool HasPattern(LayerDefinition definition) => !string.IsNullOrWhiteSpace(definition.FileStemPattern);

    private static bool MatchesPattern(string pattern, string stem) =>
        Regex.IsMatch(
            stem,
            string.Concat("^(?:", pattern, ")\\z"),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
}
