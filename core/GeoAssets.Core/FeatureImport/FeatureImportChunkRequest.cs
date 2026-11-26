using GeoAssets.Core.Models;

namespace GeoAssets.Core.FeatureImport;

/// <summary>One chunk of an import. <see cref="Features"/> holds at most <see cref="FeatureImportLimits.MaxChunkSize"/> items.</summary>
public sealed record FeatureImportChunkRequest(
    Guid OrganizationId,
    string ImportId,
    string Scope,
    IReadOnlyList<GeoFeature> Features);
