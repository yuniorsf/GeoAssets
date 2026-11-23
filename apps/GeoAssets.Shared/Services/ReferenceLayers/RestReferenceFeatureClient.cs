using System.Globalization;
using System.Net;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// <see cref="IReferenceFeatureClient"/> over the GeoAssets Server REST API (XD01-331). <paramref name="http"/>'s base
/// address is the server's <c>/api/geoassets/</c> (through the gateway's kernel catch-all) and it carries the caller's
/// token. The body is returned as the server wrote it — the map parses it natively, so the features never pass through
/// a .NET model on the client.
/// </summary>
public sealed class RestReferenceFeatureClient(HttpClient http) : IReferenceFeatureClient
{
    public async Task<ReferenceFeatureResponse> GetAsync(Guid organizationId, BoundingBox bbox, CancellationToken ct)
    {
        using var response = await http.GetAsync(RouteFor(organizationId, bbox), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden) return ReferenceFeatureResponse.Forbidden;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ReferenceFeatureResponse.Granted(string.IsNullOrWhiteSpace(json) ? "[]" : json);
    }

    /// <summary>The relative route for <paramref name="organizationId"/>'s features in <paramref name="bbox"/>, invariant-culture numbers.</summary>
    public static Uri RouteFor(Guid organizationId, BoundingBox bbox) =>
        new(string.Create(
                CultureInfo.InvariantCulture,
                $"organizations/{organizationId:D}/reference-features?bbox={bbox.MinLon:R},{bbox.MinLat:R},{bbox.MaxLon:R},{bbox.MaxLat:R}"),
            UriKind.Relative);
}
