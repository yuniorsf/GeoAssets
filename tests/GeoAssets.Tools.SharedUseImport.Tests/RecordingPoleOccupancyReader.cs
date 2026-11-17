using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;

namespace GeoAssets.Tools.SharedUseImport.Tests;

/// <summary>Passes reads through to <paramref name="inner"/> and records the size of every pole batch asked for.</summary>
internal sealed class RecordingPoleOccupancyReader(IPoleOccupancyReader inner) : IPoleOccupancyReader
{
    public List<int> BatchSizes { get; } = [];

    public Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct) => inner.GetForPoleAsync(poleFeatureId, ct);

    public Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        BatchSizes.Add(poleFeatureIds.Count);
        return inner.GetForPolesAsync(poleFeatureIds, ct);
    }

    public Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) =>
        inner.FindContractedOccupantsAsync(poleFeatureIds, ct);

    public Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct) => inner.GetPageAsync(query, ct);
}
