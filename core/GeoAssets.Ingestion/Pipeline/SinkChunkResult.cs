using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>Result of writing one chunk to a sink: rejected feature ids become <see cref="ImportIssueCodes.Rejected"/> issues.</summary>
public sealed record SinkChunkResult(int Written, IReadOnlyList<ImportIssue> Rejections);
