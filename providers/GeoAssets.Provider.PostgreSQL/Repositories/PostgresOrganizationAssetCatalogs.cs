using System.Collections.Concurrent;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
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
/// shared features are held once. Memory grows with the features of the organizations that have
/// used this process, plus the shared features once; an organization's catalog is created on
/// first use and kept for the life of the process.</para>
///
/// <para><b>Warming.</b> <see cref="WarmAsync"/> loads an organization's cache asynchronously
/// (once; again after a failure), so its first synchronous read does not fall back to the
/// provider's blocking load. <see cref="RefreshAsync"/> reloads it after a write this process made
/// outside that catalog — the asset batches the outbox relay applies (XD01-337).</para>
///
/// <para>Disposes the organizations' providers; the shared catalog belongs to whoever created it.</para>
/// </summary>
public sealed class PostgresOrganizationAssetCatalogs : IAsyncDisposable
{
    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly IAssetProvider _shared;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly OrganizationAssetCatalog _sharedOnly;
    private readonly ConcurrentDictionary<Guid, Lazy<Entry>> _entries = new();

    /// <param name="dbOptions">Options for each organization's provider (the same database as the shared catalog).</param>
    /// <param name="shared">The shared catalog: the host's provider built without a tenant.</param>
    /// <param name="loggerFactory">Creates the organizations' providers' loggers.</param>
    /// <param name="timeProvider">The organizations' providers' clock.</param>
    public PostgresOrganizationAssetCatalogs(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        IAssetProvider shared,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbOptions = dbOptions;
        _shared = shared;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _sharedOnly = new OrganizationAssetCatalog(Guid.Empty, shared, own: null);
    }

    /// <summary>
    /// The catalog as <paramref name="organizationId"/> sees it; for <see cref="Guid.Empty"/>, the
    /// shared catalog alone. Not yet warmed by <see cref="WarmAsync"/>, an organization's first
    /// cache-backed read loads its features synchronously.
    /// </summary>
    public IAssetProvider For(Guid organizationId) =>
        organizationId == Guid.Empty ? _sharedOnly : EntryFor(organizationId).Catalog;

    /// <summary>Loads <paramref name="organizationId"/>'s cache unless already loaded; nothing for <see cref="Guid.Empty"/>.</summary>
    public async Task WarmAsync(Guid organizationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty)
            return;

        await EntryFor(organizationId).WarmOnceAsync().WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reloads <paramref name="organizationId"/>'s cache from the database when this process has
    /// one; nothing otherwise (the next use loads it fresh).
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
        await entry.Provider.WarmCacheAsync(ct).ConfigureAwait(false);
    }

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
        return new Entry(provider, new OrganizationAssetCatalog(organizationId, _shared, Validating(provider)));
    }

    private PostgresAssetProvider NewProvider(Guid organizationId) =>
        new(_dbOptions, organizationId, _loggerFactory.CreateLogger<PostgresAssetProvider>(), _timeProvider);

    private static ValidatingAssetProvider Validating(PostgresAssetProvider provider) => new(provider);

    private sealed class Entry(PostgresAssetProvider provider, OrganizationAssetCatalog catalog)
    {
        private readonly Lock _gate = new();
        private Task? _warming;

        public PostgresAssetProvider Provider { get; } = provider;

        public OrganizationAssetCatalog Catalog { get; } = catalog;

        /// <summary>The one load of this organization's cache, started on first call and again after a failed one.</summary>
        public Task WarmOnceAsync()
        {
            lock (_gate)
            {
                if (_warming is null || _warming.IsFaulted || _warming.IsCanceled)
                    _warming = Provider.WarmCacheAsync(CancellationToken.None);

                return _warming;
            }
        }
    }
}
