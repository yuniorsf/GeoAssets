using System.Globalization;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.Server;

/// <summary>
/// Another organization's features as a map reference (XD01-330): one read-only route with its own reduced projection,
/// <see cref="ReferenceFeature"/> — id, geometry, label, asset type id and name — so a full <see cref="GeoFeature"/> of
/// another organization never leaves the server.
///
/// <para><c>GET {prefix}/organizations/{organizationId}/reference-features?bbox=minLon,minLat,maxLon,maxLat[&amp;assetTypeId=]</c>:</para>
/// <list type="bullet">
///   <item>Requires an authenticated caller and nothing else at the route: not the home <c>features:read</c>. Access is
///   decided by <see cref="OrganizationResourceAccess"/> for <see cref="Permission"/> on the organization's
///   <see cref="GeoFeature"/>s <b>before any data is read</b> — an active grant from it listing the permission (its
///   <c>ScopeId</c> narrowing what is returned), held at home for an organization-wide grant or through a role on that
///   organization for a grant that requires one. A caller it refuses gets <c>403</c>, the same whether or not the
///   organization exists or has features.</item>
///   <item><c>bbox</c> is mandatory (RFC 7946 order, finite, within ±180/±90, min ≤ max): <c>400</c> otherwise.</item>
///   <item>At most <see cref="GeoAssetsRestApiExtensions.MaxBoundsFeatureCount"/> features, as <c>GET /features/bounds</c>,
///   by ordinal id; only the ones the organization owns.</item>
///   <item><c>Cache-Control: private</c> on every answer: it depends on who asks.</item>
/// </list>
/// The read goes through <see cref="IReferenceFeatureQuery"/>, under the organization's tenant for that query only.
/// </summary>
public static class ReferenceFeaturesRestApiExtensions
{
    /// <summary>The permission a grant must list for its grantee to see the granting organization's reference features.</summary>
    public const string Permission = "features:reference";

    internal static void MapReferenceFeaturesApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/organizations/{{organizationId:guid}}/reference-features", async (
            Guid organizationId,
            string? bbox,
            string? assetTypeId,
            HttpContext http,
            [FromServices] OrganizationResourceAccess access,
            [FromServices] IReferenceFeatureQuery query,
            CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private";

            if (!TryParseBoundingBox(bbox, out var box))
                return Results.BadRequest("bbox=minLon,minLat,maxLon,maxLat is required: four finite numbers, longitudes within ±180, latitudes within ±90, min <= max.");

            var scope = await access.ResolveAsync(organizationId, nameof(GeoFeature), Permission, ct).ConfigureAwait(false);
            if (!scope.IsGranted)
                return Results.Json(new { reason = "Not authorized to read this organization's reference features." },
                    statusCode: StatusCodes.Status403Forbidden);

            var features = await query.GetInBoundsAsync(
                organizationId,
                box,
                new ReferenceFeatureFilter(assetTypeId, scope.ScopeIds, GeoAssetsRestApiExtensions.MaxBoundsFeatureCount),
                ct).ConfigureAwait(false);

            return Results.Json(features, opts);
        })
            .RequireAuthorization();
    }

    private static bool TryParseBoundingBox(string? raw, out BoundingBox bbox)
    {
        bbox = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Split(',');
        if (parts.Length != 4) return false;

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                return false;
        }

        var (minLon, minLat, maxLon, maxLat) = (values[0], values[1], values[2], values[3]);
        if (minLon < -180 || maxLon > 180 || minLat < -90 || maxLat > 90 || minLon > maxLon || minLat > maxLat)
            return false;

        bbox = new BoundingBox(minLon, minLat, maxLon, maxLat);
        return true;
    }
}
