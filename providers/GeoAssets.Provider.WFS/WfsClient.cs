using System.Text.Json;
using System.Xml.Linq;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// Thin HTTP client for the OGC WFS 2.0 KVP (Key-Value-Pairs) binding.
/// Supports GetCapabilities, GetFeature (with optional BBOX), and
/// DescribeFeatureType. The GetFeature output format is always
/// <c>application/json</c> (GeoJSON FeatureCollection).
/// </summary>
internal sealed class WfsClient
{
    // OGC WFS 2.0 namespace declarations used when parsing GetCapabilities XML.
    private static readonly XNamespace _wfsNs = "http://www.opengis.net/wfs/2.0";
    private static readonly XNamespace _owsNs = "http://www.opengis.net/ows/1.1";

    private static readonly JsonSerializerOptions _jsonOpts = GeoJsonSerializer.GetOptions();

    private readonly HttpClient _http;

    public WfsClient(HttpClient http) => _http = http;

    // ── GetCapabilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Fetches and parses the WFS GetCapabilities document.
    /// Returns a lightweight model with the service title and available feature types.
    /// </summary>
    public async Task<WfsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var xml = await _http.GetStringAsync(
            "?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities", ct);

        var doc  = XDocument.Parse(xml);
        var root = doc.Root!;

        var title = root
            .Element(_owsNs + "ServiceIdentification")
            ?.Element(_owsNs + "Title")?.Value
            ?? string.Empty;

        var featureTypes = root
            .Element(_wfsNs + "FeatureTypeList")
            ?.Elements(_wfsNs + "FeatureType")
            .Select(ft => new WfsFeatureType
            {
                Name       = ft.Element(_wfsNs + "Name")?.Value        ?? string.Empty,
                Title      = ft.Element(_wfsNs + "Title")?.Value       ?? string.Empty,
                DefaultCrs = ft.Element(_wfsNs + "DefaultCRS")?.Value  ?? "urn:ogc:def:crs:EPSG::4326"
            })
            .ToList()
            ?? [];

        return new WfsCapabilities
        {
            Version      = root.Attribute("version")?.Value ?? "2.0.0",
            Title        = title,
            FeatureTypes = featureTypes
        };
    }

    // ── GetFeature ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches all features of <paramref name="typeName"/> up to <paramref name="count"/> items.
    /// </summary>
    public Task<GeoFeature[]> GetFeatureAsync(
        string typeName,
        int    count       = 10_000,
        CancellationToken ct = default)
    {
        var url = BuildGetFeatureUrl(typeName, count, bbox: null);
        return FetchFeaturesAsync(url, ct);
    }

    /// <summary>
    /// Fetches features within the given bounding box (lon/lat, EPSG:4326).
    /// The BBOX parameter uses the WFS 2.0 format:
    /// <c>minLon,minLat,maxLon,maxLat,urn:ogc:def:crs:EPSG::4326</c>.
    /// </summary>
    public Task<GeoFeature[]> GetFeatureInBoundsAsync(
        string typeName,
        double minLon, double minLat, double maxLon, double maxLat,
        int    count       = 10_000,
        CancellationToken ct = default)
    {
        var bbox = FormattableString.Invariant(
            $"{minLon},{minLat},{maxLon},{maxLat},urn:ogc:def:crs:EPSG::4326");
        var url  = BuildGetFeatureUrl(typeName, count, bbox);
        return FetchFeaturesAsync(url, ct);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static string BuildGetFeatureUrl(string typeName, int count, string? bbox)
    {
        var url = $"?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature" +
                  $"&TYPENAMES={Uri.EscapeDataString(typeName)}" +
                  $"&COUNT={count}" +
                  $"&OUTPUTFORMAT={Uri.EscapeDataString("application/json")}";

        if (bbox is not null)
            url += $"&BBOX={Uri.EscapeDataString(bbox)}";

        return url;
    }

    private async Task<GeoFeature[]> FetchFeaturesAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using  var doc   = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Standard WFS GeoJSON envelope: { "type": "FeatureCollection", "features": [...] }
        if (!doc.RootElement.TryGetProperty("features", out var featuresEl))
            return [];

        return [.. featuresEl.EnumerateArray()
            .Select(MapFeature)
            .OfType<GeoFeature>()];
    }

    /// <summary>
    /// Maps a raw GeoJSON feature element to a <see cref="GeoFeature"/>.
    ///
    /// Fast path: if the element already contains <c>properties.assetTypeId</c>
    /// (our own server format) it is deserialized directly via <see cref="GeoJsonSerializer"/>.
    ///
    /// Generic path: for external WFS services the feature is mapped heuristically
    /// — common property names are promoted to <see cref="GeoFeatureProperties.Name"/>
    /// and <see cref="GeoFeatureProperties.Description"/>; everything else goes into
    /// <see cref="GeoFeatureProperties.CustomAttributes"/>.
    /// </summary>
    private static GeoFeature? MapFeature(JsonElement el)
    {
        try
        {
            el.TryGetProperty("properties", out var props);

            // ── Fast path: our own serialization format ─────────────────────
            if (props.ValueKind == JsonValueKind.Object &&
                props.TryGetProperty("assetTypeId", out _))
            {
                return JsonSerializer.Deserialize<GeoFeature>(el.GetRawText(), _jsonOpts);
            }

            // ── Generic WFS mapping ─────────────────────────────────────────
            // WFS feature ids are often "typeName.numericId" — strip the prefix.
            var rawId = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (rawId?.Contains('.') == true)
                rawId = rawId[(rawId.LastIndexOf('.') + 1)..];
            if (string.IsNullOrEmpty(rawId))
                rawId = Guid.NewGuid().ToString();

            var name        = string.Empty;
            var description = string.Empty;
            var custom      = new Dictionary<string, string>();

            if (props.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in props.EnumerateObject())
                {
                    var val = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? string.Empty
                        : p.Value.ToString();

                    switch (p.Name.ToLowerInvariant())
                    {
                        case "name": case "nombre": case "title": case "label":
                            if (string.IsNullOrEmpty(name)) name = val;
                            break;
                        case "description": case "descripcion": case "abstract": case "notes":
                            if (string.IsNullOrEmpty(description)) description = val;
                            break;
                        default:
                            custom[p.Name] = val;
                            break;
                    }
                }
            }

            GeoGeometry? geometry = null;
            if (el.TryGetProperty("geometry", out var geomEl) &&
                geomEl.ValueKind != JsonValueKind.Null)
            {
                geometry = JsonSerializer.Deserialize<GeoGeometry>(geomEl.GetRawText(), _jsonOpts);
            }

            return new GeoFeature
            {
                Id       = rawId,
                Geometry = geometry,
                Properties = new GeoFeatureProperties
                {
                    Name             = name,
                    Description      = description,
                    AssetTypeId      = AssetType.Point.Id.ToString(),
                    CustomAttributes = custom
                }
            };
        }
        catch
        {
            return null; // skip malformed features silently
        }
    }
}
