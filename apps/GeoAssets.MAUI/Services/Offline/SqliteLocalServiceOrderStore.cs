using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Offline;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>SQLCipher-backed <see cref="ILocalServiceOrderStore"/> (XD01-243).</summary>
internal sealed class SqliteLocalServiceOrderStore : ILocalServiceOrderStore
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<ServiceOrder?> GetAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalServiceOrderRow>(serviceOrderId).ConfigureAwait(false);
        return row is null ? null : JsonSerializer.Deserialize<ServiceOrder>(row.OrderJson, _opts);
    }

    public async Task<IReadOnlyList<ServiceOrder>> GetAllAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.Table<LocalServiceOrderRow>().ToListAsync().ConfigureAwait(false);
        return rows
            .Select(r => JsonSerializer.Deserialize<ServiceOrder>(r.OrderJson, _opts))
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();
    }

    public async Task UpsertAsync(ServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalServiceOrderRow { Id = order.Id, OrderJson = JsonSerializer.Serialize(order, _opts) };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }

    public async Task<ServiceOrder?> GetBaseSnapshotAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalBaseSnapshotRow>(serviceOrderId).ConfigureAwait(false);
        return row is null ? null : JsonSerializer.Deserialize<ServiceOrder>(row.OrderJson, _opts);
    }

    public async Task SaveBaseSnapshotAsync(ServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalBaseSnapshotRow { Id = order.Id, OrderJson = JsonSerializer.Serialize(order, _opts) };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }
}
