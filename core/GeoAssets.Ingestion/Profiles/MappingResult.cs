using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Profiles;

/// <summary>Result of mapping one raw record: an element, or a skip with the issue explaining it.</summary>
public sealed record MappingResult(CanonicalElement? Element, ImportIssue? Issue)
{
    public static MappingResult Mapped(CanonicalElement element) => new(element, null);

    public static MappingResult Skipped(ImportIssue issue) => new(null, issue);
}
