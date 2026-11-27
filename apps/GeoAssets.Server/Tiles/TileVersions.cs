using System.Globalization;
using System.Security.Cryptography;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// Where a tile URL's <c>{v}</c> comes from (ADR-002 D6, XD01-434). <c>{v}</c> is
/// <c>{style}-{epoch}-{flush}.{generation}</c>:
/// <list type="bullet">
///   <item><c>{style}</c> — the layer set's style hash (<see cref="TileLayerSetDescriptor.StyleHash"/>): its layers, their
///   zoom ranges, colors, layer rules and LOD thresholds, everything but the features that changes what its tiles hold.</item>
///   <item><c>{epoch}</c> — this process's, random at startup, so a restarted server (an empty cache, the counters back at
///   zero) never hands out a URL an earlier process served other bytes under.</item>
///   <item><c>{flush}</c> — bumped by <see cref="Flush"/> when a write is too large to evict tile by tile.</item>
///   <item><c>{generation}</c> — bumped by every committed write's invalidation (<see cref="Advance"/>).</item>
/// </list>
/// The part before the dot is the <see cref="CacheVersion"/>, the <see cref="Core.Rendering.TileCacheKey.LayerVersion"/>
/// the server's tile cache keys by: it survives an ordinary write, whose dirty tiles are evicted instead, so the rest stay
/// cached. The generation makes a write change the URLs a client learns from <c>style.json</c>, which is what lets a
/// tile response be <c>immutable</c>: a URL only ever answers bytes at least as new as the generation it names.
/// Process-wide and thread-safe; a single kernel process is the scope (another replica has its own epoch).
/// </summary>
public sealed class TileVersions
{
    private long _generation;
    private int _flushes;

    /// <summary>Versions under a random epoch.</summary>
    public TileVersions()
        : this(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)))
    {
    }

    /// <summary>Versions under <paramref name="epoch"/> (lowercase hex digits; a fixed one for tests).</summary>
    public TileVersions(string epoch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epoch);
        Epoch = epoch;
    }

    /// <summary>This process's epoch.</summary>
    public string Epoch { get; }

    /// <summary>The committed writes invalidated so far.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>The flushes so far.</summary>
    public int Flushes => Volatile.Read(ref _flushes);

    /// <summary>The cache version of a layer set of <paramref name="styleHash"/>: <c>{style}-{epoch}-{flush}</c>.</summary>
    public string CacheVersion(string styleHash) =>
        string.Create(CultureInfo.InvariantCulture, $"{styleHash}-{Epoch}-{Flushes}");

    /// <summary>The <c>{v}</c> to publish now for a layer set of <paramref name="styleHash"/>.</summary>
    public string PublishedVersion(string styleHash) =>
        string.Create(CultureInfo.InvariantCulture, $"{CacheVersion(styleHash)}.{Generation}");

    /// <summary>
    /// Whether a requested <paramref name="version"/> is current for a layer set of <paramref name="styleHash"/>: its cache
    /// version is today's and its generation not ahead of this process's. A response to any other <c>{v}</c> (a stale
    /// style, an earlier process, another replica) carries the current tile but must not be cached as immutable.
    /// </summary>
    public bool IsCurrent(string version, string styleHash)
    {
        ArgumentNullException.ThrowIfNull(version);

        var dot = version.LastIndexOf('.');
        return dot > 0
            && string.Equals(version[..dot], CacheVersion(styleHash), StringComparison.Ordinal)
            && long.TryParse(version.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            && generation <= Generation;
    }

    /// <summary>A committed write was invalidated: every layer set's <c>{v}</c> changes, its cache version does not.</summary>
    public void Advance() => Interlocked.Increment(ref _generation);

    /// <summary>A write too large to evict tile by tile: every cache version changes, so nothing cached before is served again.</summary>
    public void Flush() => Interlocked.Increment(ref _flushes);
}
