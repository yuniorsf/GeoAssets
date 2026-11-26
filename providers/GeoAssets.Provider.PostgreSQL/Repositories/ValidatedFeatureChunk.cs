using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>A chunk after <see cref="FeatureImportChunkValidator"/>: the features to write (one per id) and the ones refused.</summary>
internal sealed record ValidatedFeatureChunk(IReadOnlyList<GeoFeature> Accepted, IReadOnlyList<FeatureImportRejection> Rejected);
