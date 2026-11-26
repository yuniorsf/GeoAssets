using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Outcome of a run: counts per asset class and per resolution method, references left unresolved, chunks and
/// features written, and the issues (counted by code; <see cref="IssueSamples"/> keeps at most 20 per code).
/// </summary>
public sealed record ImportReport
{
    public required string ProfileId { get; init; }
    public required string ScopePrefix { get; init; }
    public IReadOnlyDictionary<string, long> ElementsByAssetClass { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> ReferencesByMethod { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public long UnresolvedReferences { get; init; }
    public IReadOnlyDictionary<string, long> IssueCounts { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyList<ImportIssue> IssueSamples { get; init; } = [];
    public int ChunksWritten { get; init; }
    public long FeaturesWritten { get; init; }
    public string PositionAccuracy { get; init; } = "none";
}
