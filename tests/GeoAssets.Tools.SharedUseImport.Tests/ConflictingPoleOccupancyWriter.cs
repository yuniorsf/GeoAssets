using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Tools.SharedUseImport.Tests;

/// <summary>A writer that always loses the race: every write throws <see cref="OccupancyConflictException"/>.</summary>
internal sealed class ConflictingPoleOccupancyWriter : IPoleOccupancyWriter
{
    public Task<PoleOccupancy> ReplaceEntriesAsync(string poleFeatureId, int expectedVersion, IReadOnlyCollection<PoleOccupancyEntry> entries, CancellationToken ct) =>
        Task.FromException<PoleOccupancy>(new OccupancyConflictException([poleFeatureId]));
}
