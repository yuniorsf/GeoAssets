using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAppliedMutationBatchStore"/> (XD01-229).
/// Implements <see cref="IAsyncDisposable"/> so a DI-created instance (e.g. via
/// <c>IDbContextFactory&lt;GeoAssetsDbContext&gt;.CreateDbContext()</c>, which the container
/// never tracks on its own) has its underlying <paramref name="db"/> disposed when the
/// request scope ends.
/// </summary>
public sealed class EFAppliedMutationBatchStore(GeoAssetsDbContext db) : IAppliedMutationBatchStore, IAsyncDisposable
{
    public ValueTask DisposeAsync() => db.DisposeAsync();


    public async Task<AssetBatchAppliedEvent?> FindAsync(Guid batchId, CancellationToken ct = default)
    {
        var row = await db.AppliedMutationBatches
            .FirstOrDefaultAsync(x => x.BatchId == batchId, ct).ConfigureAwait(false);

        return row is null ? null : ToEvent(row);
    }

    public Task RecordAsync(AssetBatchAppliedEvent applied, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(applied);

        db.AppliedMutationBatches.Add(new AppliedMutationBatchRow
        {
            BatchId = applied.BatchId,
            CatalogInstanceId = applied.CatalogInstanceId,
            OrganizationId = applied.OrganizationId,
            RequestedByService = applied.RequestedByService,
            AppliedByUserId = applied.AppliedByUserId,
            SourceOrderId = applied.SourceOrderId,
            SourceOrderTypeId = applied.SourceOrderTypeId,
            ScopeId = applied.ScopeId,
            AppliedItemsJson = JsonSerializer.Serialize(applied.AppliedItems),
            AppliedAtUtc = applied.AppliedAtUtc.UtcDateTime,
            CorrelationId = applied.CorrelationId,
        });

        return db.SaveChangesAsync(ct);
    }

    private static AssetBatchAppliedEvent ToEvent(AppliedMutationBatchRow row) => new(
        BatchId: row.BatchId,
        CatalogInstanceId: row.CatalogInstanceId,
        OrganizationId: row.OrganizationId,
        RequestedByService: row.RequestedByService,
        AppliedByUserId: row.AppliedByUserId,
        SourceOrderId: row.SourceOrderId,
        SourceOrderTypeId: row.SourceOrderTypeId,
        ScopeId: row.ScopeId,
        AppliedItems: JsonSerializer.Deserialize<List<AssetBatchAppliedItem>>(row.AppliedItemsJson) ?? [],
        AffectedBounds: null,
        AppliedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(row.AppliedAtUtc, DateTimeKind.Utc)),
        CorrelationId: row.CorrelationId);
}
