using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Tests.Selection;

/// <summary>Records what a strategy asked of <see cref="IAssetSpatialQuery"/> and answers with a canned list.</summary>
internal sealed class RecordingSpatialQuery(params GeoFeature[] answer) : IAssetSpatialQuery
{
    public int Calls { get; private set; }
    public GeoPolygon? Area { get; private set; }
    public string? AttributeKey { get; private set; }
    public IReadOnlyCollection<string>? Values { get; private set; }
    public AssetSpatialFilter? Filter { get; private set; }
    public CancellationToken Token { get; private set; }

    public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(
        GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct)
    {
        Calls++;
        Area = area;
        Filter = filter;
        Token = ct;
        return Task.FromResult<IReadOnlyList<GeoFeature>>(answer);
    }

    public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct)
    {
        Calls++;
        AttributeKey = attributeKey;
        Values = values;
        Filter = filter;
        Token = ct;
        return Task.FromResult<IReadOnlyList<GeoFeature>>(answer);
    }
}
