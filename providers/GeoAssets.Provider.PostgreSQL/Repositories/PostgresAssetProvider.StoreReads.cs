using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// Store reads (XD01-449): a provider given <see cref="OrganizationCatalogOptions"/> caches its features whole only while
/// they number at most <see cref="OrganizationCatalogOptions.FullCacheMaxFeatures"/>. Beyond that it never loads them
/// all: <see cref="GetById"/> goes through a bounded by-id cache to the primary key, and the enumerating reads run in
/// PostgreSQL over the same rows the whole cache would hold (the context's query filter), in ordinal id order, refused
/// with <see cref="CatalogEnumerationLimitException"/> beyond <see cref="OrganizationCatalogOptions.EnumerationLimit"/>
/// rows. XD01-448 measured 9.3 KiB of memory per cached feature and an <see cref="OutOfMemoryException"/> for 784 k.
///
/// <para><b>Deciding.</b> <see cref="PrepareReadsAsync"/> (a warm or a refresh) counts the rows and picks the mode; a
/// synchronous read before any of them, or the first one after a write dropped the whole cache, counts the same way, so
/// a large catalog is never loaded blindly. Only <see cref="PrepareReadsAsync"/> switches a store-served provider back to
/// a whole cache.</para>
/// </summary>
public sealed partial class PostgresAssetProvider
{
    private const string OrdinalCollation = "C";

    private readonly Lock _modeGate = new();
    private OrganizationCatalogOptions? _readLimits;
    private FeatureByIdCache? _byId;
    private volatile bool _storeReads;
    private bool _readThroughWriteContext;

    /// <summary>Whether the provider currently serves its reads from PostgreSQL rather than from a whole cache.</summary>
    internal bool ServesFromStore => _storeReads;

    /// <summary>
    /// Counts the rows and, while they are few enough, (re)loads the whole cache; otherwise serves from PostgreSQL from now
    /// on and drops the by-id cache. Without <see cref="OrganizationCatalogOptions"/>, <see cref="WarmCacheAsync"/>.
    /// </summary>
    internal async Task PrepareReadsAsync(CancellationToken ct)
    {
        if (_readLimits is null)
        {
            await WarmCacheAsync(ct).ConfigureAwait(false);
            return;
        }

        var count = await CountRowsAsync(ct).ConfigureAwait(false);
        if (count > _readLimits.EffectiveFullCacheMaxFeatures)
        {
            EnterStoreReads(count);
            return;
        }

        await WarmCacheAsync(ct).ConfigureAwait(false);
        lock (_modeGate)
            _storeReads = false;
        _byId?.Clear();
    }

    /// <summary>
    /// Serves every read from PostgreSQL through the provider's own (write) context — for a provider that lives inside
    /// one transaction, which must see its own uncommitted writes (<see cref="PostgresAssetMutationBatchApplier"/>).
    /// </summary>
    internal void ServeFromStoreThroughWriteContext()
    {
        _readLimits = new OrganizationCatalogOptions { FullCacheMaxFeatures = 0, ByIdCacheCapacity = 0 };
        _readThroughWriteContext = true;
        lock (_modeGate)
        {
            _storeReads = true;
            _cache = null;
        }
    }

    /// <summary>Whether this read goes to PostgreSQL; decides the mode first when nothing decided it yet.</summary>
    internal bool UsesStoreReads()
    {
        if (_readLimits is null) return false;
        if (_storeReads) return true;
        if (_cache is not null) return false;

        lock (_modeGate)
        {
            if (_storeReads) return true;

            var count = WithReadContext(db => db.GeoEntities.Count());
            if (count <= _readLimits.EffectiveFullCacheMaxFeatures)
                return false;

            EnterStoreReads(count);
            return true;
        }
    }

    private void EnterStoreReads(int count)
    {
        lock (_modeGate)
        {
            _storeReads = true;
            _cache = null;
            _byId ??= new FeatureByIdCache(_readLimits!.EffectiveByIdCacheCapacity);
            _byId.Clear();
        }

        LogStoreReads(_logger, count, _readLimits!.EffectiveFullCacheMaxFeatures);
    }

    private GeoFeature? StoreGetById(string id)
    {
        if (_byId is not null && _byId.TryGet(id, out var cached))
            return cached;

        var row = WithReadContext(db => db.GeoEntities.AsNoTracking().FirstOrDefault(r => r.Id == id));
        if (row is null)
            return null;

        var feature = MapToFeature(row);
        _byId?.Set(feature);
        return feature;
    }

    private List<GeoFeature> StoreEnumerate(
        string operation, Func<GeoAssetsDbContext, IQueryable<GeoEntityRow>, IQueryable<GeoEntityRow>> filter)
    {
        var limit = _readLimits!.EffectiveEnumerationLimit;
        var rows = WithReadContext(db => filter(db, db.GeoEntities.AsNoTracking())
            .OrderBy(r => EF.Functions.Collate(r.Id, OrdinalCollation))
            .Take(limit + 1)
            .ToList());
        return Bounded(operation, limit, rows);
    }

    /// <summary><see cref="Search"/>'s fields — name, description, an attribute's key or value — case-insensitively, as <see cref="GetPageAsync"/>'s SearchText.</summary>
    private List<GeoFeature> StoreSearch(string query)
    {
        var pattern = $"%{query}%";
        return StoreEnumerate(nameof(Search), (db, rows) => rows.Where(r =>
            EF.Functions.ILike(r.Name, pattern)
            || EF.Functions.ILike(r.Description, pattern)
            || db.GeoEntityAttributes.Any(a =>
                a.EntityId == r.Id && (EF.Functions.ILike(a.Key, pattern) || EF.Functions.ILike(a.Value, pattern)))));
    }

    /// <summary><see cref="GetNearby"/> in PostgreSQL: planar distance in degrees, nearest first, as the whole cache answers it.</summary>
    private List<GeoFeature> StoreNearby(GeoPoint center, double distanceDegrees)
    {
        ArgumentNullException.ThrowIfNull(center);
        var point = center.NtsGeometry;
        var limit = _readLimits!.EffectiveEnumerationLimit;
        var rows = WithReadContext(db => db.GeoEntities.AsNoTracking()
            .Where(r => r.Geom != null && r.Geom.IsWithinDistance(point, distanceDegrees))
            .OrderBy(r => r.Geom!.Distance(point))
            .ThenBy(r => EF.Functions.Collate(r.Id, OrdinalCollation))
            .Take(limit + 1)
            .ToList());
        return Bounded(nameof(GetNearby), limit, rows);
    }

    private static List<GeoFeature> Bounded(string operation, int limit, List<GeoEntityRow> rows) =>
        rows.Count > limit
            ? throw new CatalogEnumerationLimitException(operation, limit)
            : [.. rows.Select(MapToFeature)];

    /// <summary>Runs <paramref name="read"/> on a short-lived context, or on the write context inside a batch's transaction.</summary>
    private T WithReadContext<T>(Func<GeoAssetsDbContext, T> read)
    {
        if (_readThroughWriteContext)
            return read(_db);

        using var db = NewContext();
        return read(db);
    }

    private async Task<int> CountRowsAsync(CancellationToken ct)
    {
        if (_readThroughWriteContext)
            return await _db.GeoEntities.CountAsync(ct).ConfigureAwait(false);

        var db = NewContext();
        await using (db.ConfigureAwait(false))
            return await db.GeoEntities.CountAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgresAssetProvider serves {RowCount} features from PostgreSQL (more than the {FullCacheMaxFeatures} it caches whole)")]
    private static partial void LogStoreReads(ILogger logger, int rowCount, int fullCacheMaxFeatures);
}
