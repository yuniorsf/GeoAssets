using System.Collections.Concurrent;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// One <see cref="OrganizationAssetCatalog"/> per organization, for a host that serves several
/// organizations from one process (XD01-338) — <c>GeoAssets.Server</c>'s request-scoped
/// <see cref="IAssetProvider"/>.
///
/// <para><b>Caches.</b> Each organization gets its own <see cref="PostgresAssetProvider"/>, built
/// for that organization, whose cache holds that organization's features only; the shared
/// catalog (the host's existing pre-warmed provider, built without a tenant) keeps the shared
/// features. A cache built for one organization is therefore never consulted for another, and
/// shared features are held once. An organization's catalog is created on first use and kept for
/// the life of the process.</para>
///
/// <para><b>Bounded (XD01-449).</b> Only an organization with at most
/// <see cref="OrganizationCatalogOptions.FullCacheMaxFeatures"/> own features is cached whole. A larger
/// one is served from PostgreSQL (<see cref="IsQueryBacked"/>): by id through a bounded cache, its
/// enumerating reads and topology traversals in SQL, refused beyond
/// <see cref="OrganizationCatalogOptions.EnumerationLimit"/> features
/// (<see cref="GeoAssets.Core.Services.CatalogEnumerationLimitException"/>). Memory therefore grows
/// with the small organizations' features only, plus the shared features once.</para>
///
/// <para><b>Warming.</b> <see cref="WarmAsync"/> counts an organization's features and loads its
/// cache when it is small, asynchronously (once; again after a failure), so its first synchronous
/// read does not fall back to the provider's blocking load. <see cref="RefreshAsync"/> does it again
/// after a write this process made outside that catalog — the asset batches the outbox relay applies
/// (XD01-337), a bulk import's completion — so a store-served organization is only recounted, and one
/// that grew past the threshold stops being cached whole.</para>
///
/// <para>Disposes the organizations' providers; the shared catalog belongs to whoever created it.</para>
/// </summary>
public sealed class PostgresOrganizationAssetCatalogs : IOrganizationCatalogFactory, IAsyncDisposable
{
    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly IAssetProvider _shared;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ITileInvalidation? _tileInvalidation;
    private readonly OrganizationCatalogOptions _catalogOptions;
    private readonly OrganizationAssetCatalog _sharedOnly;
    private readonly ConcurrentDictionary<Guid, Lazy<Entry>> _entries = new();

    /// <param name="dbOptions">Options for each organization's provider (the same database as the shared catalog).</param>
    /// <param name="shared">The shared catalog: the host's provider built without a tenant.</param>
    /// <param name="loggerFactory">Creates the organizations' providers' loggers.</param>
    /// <param name="timeProvider">The organizations' providers' clock.</param>
    /// <param name="tileInvalidation">Where the organizations' providers report their committed feature writes' dirty tiles (XD01-432); <c>null</c> = nowhere.</param>
    /// <param name="catalogOptions">Which organizations are cached whole and how the others are served (XD01-449); <c>null</c> = the defaults.</param>
    public PostgresOrganizationAssetCatalogs(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        IAssetProvider shared,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        ITileInvalidation? tileInvalidation = null,
        OrganizationCatalogOptions? catalogOptions = null)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbOptions = dbOptions;
        _shared = shared;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _tileInvalidation = tileInvalidation;
        _catalogOptions = catalogOptions ?? new OrganizationCatalogOptions();
        _sharedOnly = new OrganizationAssetCatalog(Guid.Empty, shared, own: null);
    }

    /// <summary>
    /// The catalog as <paramref name="organizationId"/> sees it; for <see cref="Guid.Empty"/>, the
    /// shared catalog alone. Not yet warmed by <see cref="WarmAsync"/>, an organization's first
    /// cache-backed read loads its features synchronously.
    /// </summary>
    public IAssetProvider For(Guid organizationId) =>
        organizationId == Guid.Empty ? _sharedOnly : EntryFor(organizationId).Catalog;

    /// <inheritdoc cref="For"/>
    public IAssetProvider GetForOrganization(Guid organizationId) => For(organizationId);

    /// <summary>The catalog as <paramref name="organizationId"/> sees it, its cache loaded (<see cref="WarmAsync"/>) first.</summary>
    public async ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        await WarmAsync(organizationId, ct).ConfigureAwait(false);
        return For(organizationId);
    }

    /// <summary>Loads <paramref name="organizationId"/>'s cache unless already loaded; nothing for <see cref="Guid.Empty"/>.</summary>
    public async Task WarmAsync(Guid organizationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty)
            return;

        await EntryFor(organizationId).WarmOnceAsync().WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-sizes <paramref name="organizationId"/>'s catalog when this process has one, and reloads its
    /// cache when it is still small; a store-served one only drops its by-id cache (XD01-449). Nothing
    /// otherwise (the next use sizes it fresh).
    /// </summary>
    public async Task RefreshAsync(Guid organizationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty
            || !_entries.TryGetValue(organizationId, out var lazy)
            || !lazy.IsValueCreated)
        {
            return;
        }

        var entry = lazy.Value;

        // A load already under way may have read the rows before the write; wait for it so it
        // cannot overwrite the reload below.
        await entry.WarmOnceAsync().WaitAsync(ct).ConfigureAwait(false);
        await entry.Provider.PrepareReadsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether <paramref name="organizationId"/>'s catalog is served from PostgreSQL rather than cached whole (XD01-449);
    /// <c>false</c> for <see cref="Guid.Empty"/> and for an organization this process has not used or sized yet.
    /// </summary>
    public bool IsQueryBacked(Guid organizationId) =>
        organizationId != Guid.Empty
        && _entries.TryGetValue(organizationId, out var lazy)
        && lazy.IsValueCreated
        && lazy.Value.Provider.ServesFromStore;

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _entries.Values.Where(l => l.IsValueCreated))
            await lazy.Value.Provider.DisposeAsync().ConfigureAwait(false);

        _entries.Clear();
    }

    private Entry EntryFor(Guid organizationId) =>
        _entries.GetOrAdd(organizationId, id => new Lazy<Entry>(() => Create(id))).Value;

    /// <summary>The entry's provider is disposed by <see cref="DisposeAsync"/>; its validating wrapper holds nothing else.</summary>
    private Entry Create(Guid organizationId)
    {
        var provider = NewProvider(organizationId);
        return new Entry(provider, new OrganizationAssetCatalog(organizationId, _shared, Validating(provider), provider));
    }

    private PostgresAssetProvider NewProvider(Guid organizationId) =>
        new(_dbOptions, organizationId, _loggerFactory.CreateLogger<PostgresAssetProvider>(), _timeProvider, _tileInvalidation, _catalogOptions);

    private static ValidatingAssetProvider Validating(PostgresAssetProvider provider) => new(provider);

    private sealed class Entry(PostgresAssetProvider provider, OrganizationAssetCatalog catalog)
    {
        private readonly Lock _gate = new();
        private Task? _warming;

        public PostgresAssetProvider Provider { get; } = provider;

        public OrganizationAssetCatalog Catalog { get; } = catalog;

        /// <summary>The one sizing (and, for a small organization, load) of this organization's cache, started on first call and again after a failed one.</summary>
        public Task WarmOnceAsync()
        {
            lock (_gate)
            {
                if (_warming is null || _warming.IsFaulted || _warming.IsCanceled)
                    _warming = Provider.PrepareReadsAsync(CancellationToken.None);

                return _warming;
            }
        }
    }
}
