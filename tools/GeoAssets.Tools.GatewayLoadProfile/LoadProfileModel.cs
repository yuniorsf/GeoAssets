using System.Globalization;

namespace GeoAssets.Tools.GatewayLoadProfile;

/// <summary>
/// The request-count model the gateway's rate-limit defaults are derived from (XD01-393; see
/// <c>docs/perf/gateway-rate-limits.md</c>). Pure: every number below is a function of these parameters.
/// </summary>
/// <param name="ViewportWidthPx">Map area width — a 1920 px screen minus the 340 px sidebar.</param>
/// <param name="ViewportHeightPx">Map area height — a 1080 px screen minus the browser chrome and the top bar.</param>
/// <param name="TileSizePx">Leaflet's WMS tile size.</param>
/// <param name="WmsLayers">WMS layers shown at once (each requests its own tiles).</param>
/// <param name="ActionsPerMinute">Pans/zooms per minute while a user actively explores the map (peak, sustained).</param>
/// <param name="ZoomShare">The share of those actions that are zooms (a zoom repaints the whole viewport).</param>
/// <param name="PanReveal">The share of the viewport a pan reveals (the tiles it requests).</param>
/// <param name="ZoomBurstSteps">Zoom levels crossed by one scroll-wheel flick (each level repaints every layer).</param>
/// <param name="VectorEntries">Open vector provider entries — one <c>features/bounds</c> API call each per settled pan/zoom.</param>
/// <param name="Photos">Photos in one mobile evidence sync (an inspection's backlog).</param>
/// <param name="RequestsPerPhoto">API requests per photo (the upload and its evidence record).</param>
/// <param name="UploadExtraRequests">Other API requests of the same sync (checklist, status, refreshes).</param>
internal sealed record LoadProfileModel(
    int ViewportWidthPx = 1580,
    int ViewportHeightPx = 1020,
    int TileSizePx = 256,
    int WmsLayers = 2,
    double ActionsPerMinute = 30,
    double ZoomShare = 0.5,
    double PanReveal = 0.5,
    int ZoomBurstSteps = 5,
    int VectorEntries = 2,
    int Photos = 100,
    int RequestsPerPhoto = 2,
    int UploadExtraRequests = 20)
{
    /// <summary>Tile bucket burst headroom: two users behind one address flicking a zoom at once, plus 25 %.</summary>
    public const double TileBurstHeadroom = 2.5;

    /// <summary>Tile refill headroom: three users behind one address exploring at peak (or ~10 at a typical third of it).</summary>
    public const double TileRateHeadroom = 3;

    /// <summary>API bucket burst headroom: twice the upload sync plus a map zoom burst.</summary>
    public const double ApiBurstHeadroom = 2;

    /// <summary>API refill headroom: ten times one user's map API rate, so an upload backlog beyond the burst still drains.</summary>
    public const double ApiRateHeadroom = 10;

    /// <summary>Tiles one WMS layer requests to fill the viewport (the partial edge tiles on both sides included).</summary>
    public int TilesPerRepaint =>
        (DivideRoundingUp(ViewportWidthPx, TileSizePx) + 1) * (DivideRoundingUp(ViewportHeightPx, TileSizePx) + 1);

    /// <summary>Tile requests of an average pan/zoom over every WMS layer.</summary>
    public double TilesPerAction => WmsLayers * TilesPerRepaint * (ZoomShare + ((1 - ZoomShare) * PanReveal));

    /// <summary>One user's sustained tile rate while exploring at peak.</summary>
    public double TilesPerSecondPeak => TilesPerAction * ActionsPerMinute / 60;

    /// <summary>Tile requests of one scroll-wheel zoom flick.</summary>
    public int TileBurst => ZoomBurstSteps * WmsLayers * TilesPerRepaint;

    /// <summary>One user's sustained map API rate (<c>features/bounds</c>) while exploring at peak.</summary>
    public double ApiPerSecondPeak => VectorEntries * ActionsPerMinute / 60;

    /// <summary>Map API requests of one zoom flick.</summary>
    public int ApiMapBurst => ZoomBurstSteps * VectorEntries;

    /// <summary>API requests of one mobile evidence sync.</summary>
    public int UploadBurst => (Photos * RequestsPerPhoto) + UploadExtraRequests;

    /// <summary>Recommended <c>RateLimiting:Tiles:TokenLimit</c>.</summary>
    public int RecommendedTileTokenLimit => RoundUp(TileBurst * TileBurstHeadroom, 100);

    /// <summary>Recommended <c>RateLimiting:Tiles:TokensPerPeriod</c> with a 1 s period.</summary>
    public int RecommendedTileTokensPerSecond => RoundUp(TilesPerSecondPeak * TileRateHeadroom, 50);

    /// <summary>Recommended <c>RateLimiting:Api:TokenLimit</c>.</summary>
    public int RecommendedApiTokenLimit => RoundUp((UploadBurst + ApiMapBurst) * ApiBurstHeadroom, 100);

    /// <summary>Recommended <c>RateLimiting:Api:TokensPerPeriod</c> with a 1 s period.</summary>
    public int RecommendedApiTokensPerSecond => RoundUp(ApiPerSecondPeak * ApiRateHeadroom, 5);

    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"""
        Map session, per user
          tiles per repaint, one layer:  {TilesPerRepaint}  ({ViewportWidthPx}x{ViewportHeightPx} px viewport, {TileSizePx} px tiles)
          tiles per pan/zoom:            {TilesPerAction:0.#}  ({WmsLayers} layers, zoom share {ZoomShare:0.##}, a pan reveals {PanReveal:0.##})
          tile rate at peak:             {TilesPerSecondPeak:0.#} req/s  ({ActionsPerMinute:0.#} pans/zooms per minute)
          tile burst:                    {TileBurst} req  ({ZoomBurstSteps}-level zoom flick)
          map API rate at peak:          {ApiPerSecondPeak:0.##} req/s  ({VectorEntries} vector entries, one features/bounds each)
          map API burst:                 {ApiMapBurst} req
        Mobile photo sync, per technician
          upload burst:                  {UploadBurst} req  ({Photos} photos x {RequestsPerPhoto} + {UploadExtraRequests} other)
        Recommended buckets, 1 s replenishment period
          RateLimiting:Tiles  TokenLimit {RecommendedTileTokenLimit} (tile burst x {TileBurstHeadroom}), TokensPerPeriod {RecommendedTileTokensPerSecond} (tile rate x {TileRateHeadroom})
          RateLimiting:Api    TokenLimit {RecommendedApiTokenLimit} ((upload + map API burst) x {ApiBurstHeadroom}), TokensPerPeriod {RecommendedApiTokensPerSecond} (map API rate x {ApiRateHeadroom})
        """);

    private static int DivideRoundingUp(int value, int divisor) => (value + divisor - 1) / divisor;

    private static int RoundUp(double value, int step) => (int)(Math.Ceiling(value / step) * step);
}
