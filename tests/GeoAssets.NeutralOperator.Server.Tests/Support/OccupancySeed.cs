using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>Writes pole occupancy into the host's database through the vertical's own store, as the pole owner.</summary>
internal static class OccupancySeed
{
    /// <summary>Records <paramref name="occupants"/> on the contracted slots of <paramref name="pole"/>, owned by <paramref name="owner"/>.</summary>
    public static async Task OccupyAsync(string connectionString, Guid owner, string pole, params Guid[] occupants)
    {
        await using var db = new NeutralOperatorDbContext(
            new DbContextOptionsBuilder<NeutralOperatorDbContext>().UseNpgsql(connectionString, NeutralOperatorDbContext.ConfigureMigrations).Options,
            new FixedTenantContext(owner));
        var store = new EFPoleOccupancyRepository(db, TimeProvider.System);

        var entries = occupants
            .Select((occupant, index) => new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(owner).OnSlot(index + 1).OccupiedBy(occupant).Build())
            .ToArray();
        await store.ReplaceEntriesAsync(pole, PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
    }
}
