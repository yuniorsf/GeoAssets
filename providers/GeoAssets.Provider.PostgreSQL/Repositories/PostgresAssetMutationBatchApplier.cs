using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// PostgreSQL implementation of <see cref="IAssetMutationBatchApplier"/> (XD01-337): applies a
/// batch through the provider's existing write path — <see cref="IdempotentMutationBatchApplier"/>
/// over <see cref="ValidatingAssetProvider"/> over <see cref="PostgresAssetProvider"/>, recording
/// the batch in <see cref="EFAppliedMutationBatchStore"/> — all on one short-lived
/// <see cref="GeoAssetsDbContext"/> and inside one transaction.
///
/// <para><b>Tenant.</b> The context is built for the batch's organization, which scopes the write
/// the same three ways <see cref="PostgresAssetSpatialQuery"/> scopes a read: the context's query
/// filter (a feature of another organization is not found, so its mutation fails), the
/// transaction stamped by <see cref="TenantTransactionInterceptor"/> for the
/// <c>geo_entity</c>/<c>geo_entity_attribute</c>/<c>applied_mutation_batches</c> Row-Level
/// Security policies, and the batch record carrying that organization. A batch without an
/// organization is refused: there is no tenant to write it under.</para>
///
/// <para><b>Once per batch.</b> The idempotency check, every mutation and the batch record commit
/// together. A redelivered batch finds its record and changes nothing; two deliveries racing each
/// other cannot both commit, because the second record collides with the first on its key and
/// rolls its transaction back — its retry then finds the record.</para>
///
/// <para><b>Cost.</b> <see cref="PostgresAssetProvider"/> reads the organization's features into
/// its cache on first use, so each batch reads that organization's catalog once. The Server
/// host's singleton provider is not used: it is built without a tenant and only ever sees
/// organization-less rows.</para>
/// </summary>
public sealed class PostgresAssetMutationBatchApplier : IAssetMutationBatchApplier
{
    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    /// <param name="dbOptions">
    /// Options for the per-batch contexts. Must not enable a retrying execution strategy: each
    /// batch runs in an explicit transaction.
    /// </param>
    /// <param name="loggerFactory">Creates the per-batch <see cref="PostgresAssetProvider"/>'s logger.</param>
    /// <param name="timeProvider">Stamps the features' update time and the batch record.</param>
    public PostgresAssetMutationBatchApplier(
        DbContextOptions<GeoAssetsDbContext> dbOptions, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbOptions = dbOptions;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    public async Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.OrganizationId == Guid.Empty)
            throw new ArgumentException($"Asset mutation batch '{batch.BatchId}' names no organization to apply it under.", nameof(batch));
        ct.ThrowIfCancellationRequested();

        var tenant = new BatchOrganization(batch.OrganizationId);
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(_dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(tenant))
            .Options;

        // The provider, its validating decorator and the batch store all share — and on disposal
        // dispose — the one context, so the batch and its record go through one transaction.
        var db = new GeoAssetsDbContext(options, tenant);
        var postgres = new PostgresAssetProvider(db, options, _loggerFactory.CreateLogger<PostgresAssetProvider>(), _timeProvider);
        var catalog = new ValidatingAssetProvider(postgres);
        var store = new EFAppliedMutationBatchStore(db);
        await using (db.ConfigureAwait(false))
        await using (postgres.ConfigureAwait(false))
        await using (catalog.ConfigureAwait(false))
        await using (store.ConfigureAwait(false))
        {
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var applied = await new IdempotentMutationBatchApplier(catalog, store, _timeProvider)
                    .ApplyAsync(batch, ct)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return applied;
            }
        }
    }

    private sealed class BatchOrganization(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
