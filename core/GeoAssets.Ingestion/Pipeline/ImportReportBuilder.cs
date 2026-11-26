namespace GeoAssets.Ingestion.Pipeline;

/// <summary>Snapshots a run's counters, issues and chunk totals into an <see cref="ImportReport"/>.</summary>
internal static class ImportReportBuilder
{
    public static ImportReport Build(
        ImportRequest request,
        ImportStatistics statistics,
        IssueLog issues,
        ChunkWriter chunks,
        string positionAccuracy) => new()
        {
            ProfileId = request.Profile.Id,
            ScopePrefix = request.ScopePrefix,
            ElementsByAssetClass = new Dictionary<string, long>(statistics.ElementsByAssetClass, StringComparer.Ordinal),
            ReferencesByMethod = new Dictionary<string, long>(statistics.ReferencesByMethod, StringComparer.Ordinal),
            UnresolvedReferences = statistics.UnresolvedReferences,
            IssueCounts = new Dictionary<string, long>(issues.Counts, StringComparer.Ordinal),
            IssueSamples = [.. issues.Samples],
            ChunksWritten = chunks.ChunksWritten,
            FeaturesWritten = chunks.FeaturesWritten,
            PositionAccuracy = positionAccuracy,
        };
}
