using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// Where a tile URL's <c>{v}</c> comes from (ADR-002 D6, XD01-434, XD01-458). <c>{v}</c> is
/// <c>{style}-{epoch}-{flush}.{generation}</c>:
/// <list type="bullet">
///   <item><c>{style}</c> — the layer set's style hash (<see cref="TileLayerSetDescriptor.StyleHash"/>): its layers, their
///   zoom ranges, colors, layer rules and LOD thresholds, everything but the features that changes what its tiles hold.</item>
///   <item><c>{epoch}</c> — random: this process's at startup, or, with <see cref="TileVersionSync"/>, the one every replica
///   over the database shares — so a server whose counters started again from zero never hands out a URL an earlier one
///   served other bytes under.</item>
///   <item><c>{flush}</c> — bumped by <see cref="Flush"/> when a write is too large to evict tile by tile (and, shared, by
///   each replica's start).</item>
///   <item><c>{generation}</c> — the caller's organization's (<see cref="GenerationOf"/>): the shared features' generation
///   (<see cref="Guid.Empty"/>, which every scope sees) plus the organization's own. A committed write advances only the
///   scopes that can see it (<see cref="Advance(IEnumerable{Guid})"/>), so another organization's <c>{v}</c> stays.</item>
/// </list>
/// The part before the dot is the <see cref="CacheVersion"/>, the <see cref="Core.Rendering.TileCacheKey.LayerVersion"/>
/// the server's tile cache keys by: it survives an ordinary write, whose dirty tiles are evicted instead, so the rest stay
/// cached. The generation makes a write change the URLs a client learns from <c>style.json</c>, which is what lets a
/// tile response be <c>immutable</c>: a URL only ever answers bytes at least as new as the generation it names. Every
/// value only grows: <see cref="Adopt(TileVersionSnapshot)"/> keeps the larger of what it holds and what it is given.
/// Process-wide and thread-safe.
/// </summary>
public sealed class TileVersions
{
    private readonly ConcurrentDictionary<Guid, long> _generations = new();
    private long _changes;
    private long _flushes;
    private string _epoch;

    /// <summary>Versions under a random epoch.</summary>
    public TileVersions()
        : this(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)))
    {
    }

    /// <summary>Versions under <paramref name="epoch"/> (lowercase hex digits; a fixed one for tests).</summary>
    public TileVersions(string epoch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epoch);
        _epoch = epoch;
    }

    /// <summary>The epoch.</summary>
    public string Epoch => Volatile.Read(ref _epoch);

    /// <summary>
    /// The invalidations this process has applied so far — its own writes' and, shared, its peers' — in every scope. The
    /// tile endpoint drops a tile it drew while this moved, since the drawing may predate the write.
    /// </summary>
    public long Generation => Interlocked.Read(ref _changes);

    /// <summary>The flushes so far.</summary>
    public long Flushes => Interlocked.Read(ref _flushes);

    /// <summary>The scopes whose generation this process holds.</summary>
    public IReadOnlyCollection<Guid> KnownScopes => [.. _generations.Keys];

    /// <summary>The cache version of a layer set of <paramref name="styleHash"/>: <c>{style}-{epoch}-{flush}</c>.</summary>
    public string CacheVersion(string styleHash) =>
        string.Create(CultureInfo.InvariantCulture, $"{styleHash}-{Epoch}-{Flushes}");

    /// <summary>
    /// The generation of <paramref name="organizationId"/>'s scope: the shared features' plus its own
    /// (<see cref="Guid.Empty"/>, an organization-less caller: the shared features' alone).
    /// </summary>
    public long GenerationOf(Guid organizationId) =>
        Own(Guid.Empty) + (organizationId == Guid.Empty ? 0 : Own(organizationId));

    /// <summary>Whether this process holds <paramref name="scope"/>'s generation.</summary>
    public bool Knows(Guid scope) => _generations.ContainsKey(scope);

    /// <summary>The <c>{v}</c> to publish now for a layer set of <paramref name="styleHash"/>, to an organization-less caller.</summary>
    public string PublishedVersion(string styleHash) => PublishedVersion(styleHash, Guid.Empty);

    /// <summary>The <c>{v}</c> to publish now for a layer set of <paramref name="styleHash"/>, to a caller of <paramref name="organizationId"/>.</summary>
    public string PublishedVersion(string styleHash, Guid organizationId) =>
        string.Create(CultureInfo.InvariantCulture, $"{CacheVersion(styleHash)}.{GenerationOf(organizationId)}");

    /// <summary>Whether <paramref name="version"/> is current for a layer set of <paramref name="styleHash"/>, to an organization-less caller.</summary>
    public bool IsCurrent(string version, string styleHash) => IsCurrent(version, styleHash, Guid.Empty);

    /// <summary>
    /// Whether a requested <paramref name="version"/> is current for a layer set of <paramref name="styleHash"/>, to a caller
    /// of <paramref name="organizationId"/>: its cache version is today's and its generation not ahead of that scope's. A
    /// response to any other <c>{v}</c> (a stale style, an earlier epoch or flush, a replica further ahead) carries the
    /// current tile but must not be cached as immutable.
    /// </summary>
    public bool IsCurrent(string version, string styleHash, Guid organizationId)
    {
        ArgumentNullException.ThrowIfNull(version);

        var dot = version.LastIndexOf('.');
        return dot > 0
            && string.Equals(version[..dot], CacheVersion(styleHash), StringComparison.Ordinal)
            && long.TryParse(version.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            && generation <= GenerationOf(organizationId);
    }

    /// <summary>A committed write of the shared features was invalidated: every scope's <c>{v}</c> changes, its cache version does not.</summary>
    public void Advance() => Advance([Guid.Empty]);

    /// <summary>
    /// A committed write visible to <paramref name="scopes"/> was invalidated: their <c>{v}</c> changes (every scope's for
    /// <see cref="Guid.Empty"/>), no cache version does.
    /// </summary>
    public void Advance(IEnumerable<Guid> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        foreach (var scope in scopes.Distinct())
            _generations.AddOrUpdate(scope, 1, (_, generation) => generation + 1);

        Invalidated();
    }

    /// <summary>A write too large to evict tile by tile: every cache version changes, so nothing cached before is served again.</summary>
    public void Flush() => Interlocked.Increment(ref _flushes);

    /// <summary>An invalidation whose versions arrive later (<see cref="Adopt(IReadOnlyDictionary{Guid, long}, long?)"/>): moves <see cref="Generation"/> only.</summary>
    public void Invalidated() => Interlocked.Increment(ref _changes);

    /// <summary>The shared versions read from the store: its epoch, and the larger of each value held and read.</summary>
    public void Adopt(TileVersionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Epoch, nameof(snapshot));

        Volatile.Write(ref _epoch, snapshot.Epoch);
        Adopt(snapshot.Generations, snapshot.Flushes);
    }

    /// <summary>Shared values (a change's or a read's): the larger of each value held and given — never one going backwards.</summary>
    public void Adopt(IReadOnlyDictionary<Guid, long> generations, long? flushes)
    {
        ArgumentNullException.ThrowIfNull(generations);

        foreach (var (scope, value) in generations)
            _generations.AddOrUpdate(scope, value, (_, generation) => Math.Max(generation, value));

        if (flushes is not { } flushed) return;

        var current = Interlocked.Read(ref _flushes);
        while (flushed > current)
        {
            var seen = Interlocked.CompareExchange(ref _flushes, flushed, current);
            if (seen == current) break;
            current = seen;
        }
    }

    private long Own(Guid scope) => _generations.TryGetValue(scope, out var generation) ? generation : 0;
}
