using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// <c>GET /tiles/lod-calibration[?assetTypes={id},{id}…]</c> (ADR-002 D6/D7, XD01-439): the density-based LOD calibration of
/// the caller's organization, on demand. For each asset type the caller sees (or each one named, in tile layer order, at most
/// <see cref="MaxAssetTypesPerRequest"/>), <see cref="LodCalibrator"/> measures features and vertices per tile through the
/// host's <see cref="ITileDensitySampler"/> (the PostGIS one: bounded reads, statement timeout, the caller's tenant scope)
/// and answers the recommended <see cref="LodThresholds"/> next to the current ones, with the
/// <c>GeoAssets:Tiles:Lod:Layers:{assetTypeId}:*</c> settings that would apply the changed ones. Nothing is applied: the
/// served tiles change only when an operator changes the configuration and restarts (where the startup validation checks
/// it). Each recommendation is also logged.
/// <para>Requires <c>features:read</c> and <see cref="AdminPermission"/> (an organization's Administrator): a calibration
/// costs a few hundred bounded reads, so it is not every reader's to start. 400 for an asset type id the caller does not
/// see; 504 when a statement runs past its timeout. Like the tile endpoints it never loads the organization catalog.</para>
/// </summary>
public static partial class TileLodCalibrationEndpoint
{
    /// <summary>The route.</summary>
    public const string Route = TileEndpointExtensions.Prefix + "/lod-calibration";

    /// <summary>The administrator permission a caller needs besides <c>features:read</c>.</summary>
    public const string AdminPermission = "organizations:edit";

    /// <summary>The most asset types one request calibrates.</summary>
    public const int MaxAssetTypesPerRequest = 32;

    private const string LoggerCategory = "GeoAssets.Server.Tiles.LodCalibration";

    /// <summary>Maps the route.</summary>
    public static IEndpointRouteBuilder MapTileLodCalibration(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet(Route, (Func<HttpContext, Task<IResult>>)CalibrateAsync)
            .RequireAuthorization("features:read", AdminPermission)
            .WithoutOrganizationCatalogWarmup();
        return routes;
    }

    private static async Task<IResult> CalibrateAsync(HttpContext http)
    {
        var services = http.RequestServices;
        var serving = services.GetRequiredService<TileServing>();
        var requested = http.Request.Query["assetTypes"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = TileLayerSetDescriptor.Resolve(TileLayerSetDescriptor.AllAssetTypes, services.GetRequiredService<IAssetProvider>(), serving.Lod)?.AssetTypes ?? [];
        var (types, unknown) = Select(visible, requested);
        if (unknown.Count > 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown asset type",
                detail: $"The caller sees no asset type {string.Join(", ", unknown)}.");
        }

        var calibrated = types.Take(MaxAssetTypesPerRequest).ToList();
        var omitted = types.Skip(MaxAssetTypesPerRequest).Select(TileLayerSetDescriptor.LayerIdOf).ToList();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        var organizationId = services.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty;
        var calibrator = new LodCalibrator(services.GetRequiredService<ITileDensitySampler>(), serving.Options.Calibration);
        var ct = http.RequestAborted;
        var started = serving.TimeProvider.GetTimestamp();
        var outcome = TileMetrics.Failed;
        try
        {
            var results = new List<TileLodCalibrationResult>(calibrated.Count);
            foreach (var type in calibrated)
            {
                var layerId = TileLayerSetDescriptor.LayerIdOf(type);
                var calibration = await calibrator.CalibrateAsync(layerId, type.MinZoom, type.MaxZoom, ct).ConfigureAwait(false);
                var result = Result(type, calibration, serving.Lod.GetThresholds(layerId));
                results.Add(result);
                Report(logger, organizationId, result);
            }

            outcome = TileMetrics.Completed;
            return Results.Ok(new TileLodCalibrationResponse(organizationId, calibrator.Settings, results, omitted, Configuration(results)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = TileMetrics.Cancelled;
            return Results.Empty;
        }
        catch (TimeoutException ex)
        {
            outcome = TileMetrics.TimedOut;
            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "LOD calibration timed out",
                detail: $"{ex.Message} Calibrate fewer asset types at a time (?assetTypes=) or lower GeoAssets:Tiles:Calibration:SampleTiles / MaxFeaturesPerTile.");
        }
        finally
        {
            TileMetrics.Calibrated(outcome, serving.TimeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>The visible types <paramref name="requested"/> names (all when it names none), in their order, and the ids it names that are not visible.</summary>
    private static (IReadOnlyList<AssetType> Types, IReadOnlyList<string> Unknown) Select(IReadOnlyList<AssetType> visible, string[] requested)
    {
        if (requested.Length == 0) return (visible, []);

        var unknown = requested.Where(id => !visible.Any(t => string.Equals(TileLayerSetDescriptor.LayerIdOf(t), id, StringComparison.OrdinalIgnoreCase))).ToList();
        return ([.. visible.Where(t => requested.Contains(TileLayerSetDescriptor.LayerIdOf(t), StringComparer.OrdinalIgnoreCase))], unknown);
    }

    private static TileLodCalibrationResult Result(AssetType type, AssetTypeLodCalibration calibration, LodThresholds current)
    {
        LodThresholds? recommended = calibration.Recommended is { } r ? r with { MinZoom = current.MinZoom, MaxZoom = current.MaxZoom } : null;
        var changed = recommended is { } next && DrawnZooms(type).Any(z => next.Decide(z) != current.Decide(z));
        TileMetrics.Recommended(recommended is null ? TileMetrics.NoRecommendation : changed ? TileMetrics.Changed : TileMetrics.Unchanged);
        return new TileLodCalibrationResult(
            calibration.AssetTypeId, type.Name, type.MinZoom, type.MaxZoom, calibration.Seeds, current, recommended, changed, calibration.Densities);
    }

    private static IEnumerable<int> DrawnZooms(AssetType type)
    {
        for (var zoom = TileAddress.MinZoom; zoom <= TileAddress.MaxZoom; zoom++)
        {
            if (AssetTypeZoomRange.Contains(type.MinZoom, type.MaxZoom, zoom)) yield return zoom;
        }
    }

    private static Dictionary<string, string> Configuration(IEnumerable<TileLodCalibrationResult> results)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (!result.Changed || result.Recommended is not { } recommended) continue;

            var prefix = $"{TileServingOptions.SectionKey}:Lod:Layers:{result.AssetTypeId}:";
            settings[prefix + "RasterFromZoom"] = recommended.RasterFromZoom.ToString(CultureInfo.InvariantCulture);
            settings[prefix + "VectorFromZoom"] = recommended.VectorFromZoom.ToString(CultureInfo.InvariantCulture);
            if (recommended.MinZoom is { } min) settings[prefix + "MinZoom"] = min.ToString(CultureInfo.InvariantCulture);
            if (recommended.MaxZoom is { } max) settings[prefix + "MaxZoom"] = max.ToString(CultureInfo.InvariantCulture);
        }

        return settings;
    }

    private static void Report(ILogger logger, Guid organizationId, TileLodCalibrationResult result)
    {
        if (result.Recommended is not { } recommended)
        {
            LogNothingMeasured(logger, result.AssetTypeId, organizationId);
            return;
        }

        var densest = result.Zooms.Count == 0 ? null : result.Zooms[^1];
        LogRecommendation(
            logger,
            result.AssetTypeId,
            organizationId,
            recommended.RasterFromZoom,
            recommended.VectorFromZoom,
            result.Current.RasterFromZoom,
            result.Current.VectorFromZoom,
            result.Changed,
            densest?.Zoom ?? -1,
            densest?.Vertices.P95 ?? 0);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "LOD calibration of asset type {AssetTypeId} for organization {OrganizationId}: recommended raster from z{RecommendedRasterFromZoom}, vector from z{RecommendedVectorFromZoom} (current raster from z{CurrentRasterFromZoom}, vector from z{CurrentVectorFromZoom}; changed: {Changed}); lowest zoom measured z{LowestMeasuredZoom}, p95 {LowestZoomP95Vertices} vertices per tile there")]
    private static partial void LogRecommendation(
        ILogger logger, string assetTypeId, Guid organizationId, int recommendedRasterFromZoom, int recommendedVectorFromZoom,
        int currentRasterFromZoom, int currentVectorFromZoom, bool changed, int lowestMeasuredZoom, long lowestZoomP95Vertices);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "LOD calibration of asset type {AssetTypeId} for organization {OrganizationId}: nothing to measure (no feature in scope, or not drawn at the calibrated zooms)")]
    private static partial void LogNothingMeasured(ILogger logger, string assetTypeId, Guid organizationId);
}
