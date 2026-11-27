using System.Diagnostics.Metrics;
using GeoAssets.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// How this provider's feature writers hand a committed write's dirty tiles to the host's <see cref="ITileInvalidation"/>
/// (ADR-002 D6, XD01-432): called only once the write's transaction has committed, and never failing — a failure (or a
/// cancellation) is logged and counted, never thrown, since the write stands whatever invalidation does. The token is
/// never the request's: the write is committed, so its tiles must be invalidated even when the caller has gone. Counter
/// <c>geoassets.tiles.invalidations</c>, tags <c>writer</c> (<see cref="ProviderWriter"/>|<see cref="MutationBatchWriter"/>|<see cref="BulkImportWriter"/>)
/// and <c>outcome</c> (<see cref="Published"/>|<see cref="Failed"/>) — never an organization.
/// </summary>
internal sealed partial class AfterCommitTileInvalidation
{
    /// <summary>The meter the counter is created on — the domain metrics' meter, which the host's OpenTelemetry pipeline exports.</summary>
    public const string MeterName = "GeoAssets";

    public const string CounterName = "geoassets.tiles.invalidations";

    /// <summary>A write of <see cref="PostgresAssetProvider"/> outside a transaction of its caller's.</summary>
    public const string ProviderWriter = "provider";

    /// <summary>A batch of <see cref="PostgresAssetMutationBatchApplier"/>.</summary>
    public const string MutationBatchWriter = "mutation-batch";

    /// <summary>A chunk or completion of <see cref="PostgresFeatureBulkImporter"/>.</summary>
    public const string BulkImportWriter = "bulk-import";

    public const string Published = "published";

    public const string Failed = "failed";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Invalidations = Meter.CreateCounter<long>(
        CounterName,
        unit: "{writes}",
        description: "Committed feature writes whose dirty tiles were handed to ITileInvalidation (tags: writer, outcome=published|failed).");

    private readonly ITileInvalidation _inner;
    private readonly ILogger _logger;

    private AfterCommitTileInvalidation(ITileInvalidation inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    /// <summary>A publisher to <paramref name="inner"/>, or <c>null</c> when there is none (nothing to track).</summary>
    public static AfterCommitTileInvalidation? For(ITileInvalidation? inner, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return inner is null ? null : new AfterCommitTileInvalidation(inner, logger);
    }

    /// <summary>
    /// Hands <paramref name="regions"/> (one committed write of <paramref name="organizationId"/>'s features;
    /// <see cref="Guid.Empty"/> = the shared ones, XD01-458) to the invalidation; nothing when there are none. Never throws.
    /// </summary>
    public async Task PublishAsync(IReadOnlyCollection<DirtyTileRegion> regions, Guid organizationId, string writer)
    {
        if (regions.Count == 0) return;

        try
        {
            await _inner.InvalidateAsync(regions, organizationId, CancellationToken.None).ConfigureAwait(false);
            Count(writer, Published);
        }
        catch (Exception ex)
        {
            LogInvalidationFailed(_logger, ex, writer, regions.Count);
            Count(writer, Failed);
        }
    }

    private static void Count(string writer, string outcome) =>
        Invalidations.Add(
            1,
            new KeyValuePair<string, object?>("writer", writer),
            new KeyValuePair<string, object?>("outcome", outcome));

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Tile invalidation failed after a committed {Writer} write ({RegionCount} regions); the write stands and those tiles stay stale until they are invalidated again")]
    private static partial void LogInvalidationFailed(ILogger logger, Exception ex, string writer, int regionCount);
}
