using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Integrations.Scada;
using GeoAssets.FieldService.Agents.Dispatch;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Answers this host's narrow asset-read ports — <see cref="IAssetProximityQuery"/> (geofence
/// arrival), <see cref="IAssetTopologyQuery"/> (SCADA outage dispatch) and
/// <see cref="IDispatchAssetQuery"/> (dynamic dispatch advisor) — over the Core GeoAssets Server's
/// API through <see cref="IGeoCoreClient"/>, so this host never opens the core asset database
/// (XD01-386, Invariant 1 — Core Database Encapsulation).
///
/// Lives in the host because it is the one assembly that sees all three ports without a cycle:
/// <c>GeoAssets.FieldService.Application</c> and <c>GeoAssets.Integrations.Scada</c> don't
/// reference each other, and <c>GeoAssets.Provider.Rest</c> must not depend on verticals.
///
/// Organization scoping: when the request being served has an organization
/// (<see cref="ITenantContext.CurrentOrganizationId"/> not empty), feature and descendant reads go
/// through <see cref="IOrganizationScopedGeoCoreClient"/> — the core's service-caller-only
/// <c>organizations/{organizationId}/features/…</c> routes — so they see exactly what that
/// organization sees; without one (background work, an organization-less caller) they use
/// <see cref="IGeoCoreClient"/>, the shared view this host's service principal gets. Listing
/// features by asset type has no organization-scoped route, so it always uses the shared view.
///
/// Fails closed: every core call is bounded by <see cref="DefaultRequestTimeout"/> (the REST
/// clients themselves configure no timeout or retry policy beyond <see cref="HttpClient"/>'s
/// 100 s default, so the bound is applied here, per client call — a paged client call shares one
/// bound), and a transport failure, non-success status or that timeout is rethrown as
/// <see cref="GeoCoreUnavailableException"/> — never answered as "not found" or "not within
/// distance". The caller's own cancellation still propagates as is.
/// </summary>
internal sealed class GeoCoreAssetQueryAdapter(
    IGeoCoreClient geoCore,
    IOrganizationScopedGeoCoreClient organizationScopedGeoCore,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    TimeSpan? requestTimeout = null) : IAssetProximityQuery, IAssetTopologyQuery, IDispatchAssetQuery
{
    /// <summary>Upper bound on one round trip to the core.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound on the features one traversal or type listing collects (descendant ids walked,
    /// features of a type listed). A real feeder segment or crew roster is orders of magnitude
    /// smaller; reaching the cap means a misconfigured topology or asset type, and the walk stops
    /// there rather than pulling the whole network across the wire.
    /// </summary>
    public const int MaxFeatures = 10_000;

    /// <summary>Feature ids per neighbor-batch request and features per query page.</summary>
    public const int PageSize = 500;

    private readonly TimeSpan _requestTimeout = requestTimeout ?? DefaultRequestTimeout;

    public async Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureId);
        ArgumentNullException.ThrowIfNull(point);

        var feature = await GetFeatureAsync(featureId, ct).ConfigureAwait(false);
        return feature?.Geometry is not null
            && GeodeticDistanceCalculator.IsWithinDistance(feature.Geometry.Centroid, point, toleranceMeters);
    }

    public Task<GeoFeature?> GetFeatureAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var organizationId = tenantContext.CurrentOrganizationId;
        return organizationId != Guid.Empty
            ? CallCoreAsync(token => organizationScopedGeoCore.GetFeatureAsync(organizationId, id, token), ct)
            : CallCoreAsync(token => geoCore.GetFeatureByIdAsync(id, token), ct);
    }

    /// <summary>
    /// With an organization, the core's organization-scoped descendants route (paged by the
    /// client, capped at its own 10 000 features). Without one, breadth-first over the core's
    /// shared neighbor-batch endpoint, one request per
    /// <see cref="PageSize"/> ids of each level, with a visited set (cycles terminate) and the
    /// <see cref="MaxFeatures"/> cap; then the reached features are fetched by id, in walk order.
    /// The starting feature is excluded unless a cycle leads back to it — same as
    /// <c>TopoGraph.GetDescendants</c>.
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(string id, string edgeKind, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var organizationId = tenantContext.CurrentOrganizationId;
        if (organizationId != Guid.Empty)
        {
            return await CallCoreAsync(
                token => organizationScopedGeoCore.GetDescendantsAsync(organizationId, id, edgeKind, token), ct)
                .ConfigureAwait(false);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var reached = new List<string>();
        List<string> frontier = [id];

        while (frontier.Count > 0 && reached.Count < MaxFeatures)
        {
            var next = new List<string>();
            foreach (var chunk in frontier.Chunk(PageSize))
            {
                var neighbors = await CallCoreAsync(
                    token => geoCore.GetNeighborIdsBatchAsync(chunk, edgeKind, targetAssetTypeId: null, token), ct)
                    .ConfigureAwait(false);

                foreach (var source in chunk)
                {
                    if (!neighbors.TryGetValue(source, out var targets)) continue;
                    foreach (var target in targets)
                    {
                        if (reached.Count >= MaxFeatures) break;
                        if (!visited.Add(target)) continue;
                        reached.Add(target);
                        next.Add(target);
                    }
                }
            }

            frontier = next;
        }

        return await GetFeaturesByIdsAsync(reached, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GeoFeature>> GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetTypeId);

        var features = new List<GeoFeature>();
        while (features.Count < MaxFeatures)
        {
            var query = new AssetQuery { AssetTypeId = assetTypeId, Skip = features.Count, Take = PageSize };
            var page = await CallCoreAsync(token => geoCore.QueryFeaturesAsync(query, token), ct).ConfigureAwait(false);
            features.AddRange(page.Items.Take(MaxFeatures - features.Count));

            if (page.Items.Count < PageSize || features.Count >= page.TotalCount) break;
        }

        return features;
    }

    private async Task<IReadOnlyList<GeoFeature>> GetFeaturesByIdsAsync(List<string> ids, CancellationToken ct)
    {
        var byId = new Dictionary<string, GeoFeature>(ids.Count, StringComparer.Ordinal);
        foreach (var chunk in ids.Chunk(PageSize))
        {
            var query = new AssetQuery { FeatureIds = chunk, Take = chunk.Length };
            var page = await CallCoreAsync(token => geoCore.QueryFeaturesAsync(query, token), ct).ConfigureAwait(false);
            foreach (var feature in page.Items)
                byId[feature.Id] = feature;
        }

        var ordered = new List<GeoFeature>(byId.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var feature))
                ordered.Add(feature);
        }

        return ordered;
    }

    private async Task<T> CallCoreAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(_requestTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await call(linked.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GeoCoreUnavailableException(
                "The Core GeoAssets Server could not be reached or answered with an error status.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GeoCoreUnavailableException(
                "The Core GeoAssets Server did not answer within "
                + _requestTimeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms.", ex);
        }
    }
}
