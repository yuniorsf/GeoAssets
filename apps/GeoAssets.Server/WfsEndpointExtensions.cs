using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;

namespace GeoAssets.Server;

/// <summary>
/// Maps an OGC WFS 2.0.0 KVP endpoint onto any <see cref="IEndpointRouteBuilder"/>.
/// The registered <see cref="IAssetProvider"/> (backed by PostGIS via
/// <c>GeoAssets.Provider.PostgreSQL</c>) serves all feature requests.
///
/// Supported operations:
/// <list type="bullet">
///   <item><term>GetCapabilities</term><description>Returns a WFS 2.0 capabilities XML document.</description></item>
///   <item><term>GetFeature</term><description>Returns a GeoJSON FeatureCollection. Supports <c>TYPENAMES</c>, <c>COUNT</c>, <c>STARTINDEX</c>, and <c>BBOX</c>, paged in the store with or without BBOX (XD01-449, XD01-478).</description></item>
///   <item><term>DescribeFeatureType</term><description>Returns a JSON schema for the GeoFeature type.</description></item>
/// </list>
///
/// Example requests:
/// <code>
/// GET /wfs?SERVICE=WFS&amp;VERSION=2.0.0&amp;REQUEST=GetCapabilities
/// GET /wfs?SERVICE=WFS&amp;VERSION=2.0.0&amp;REQUEST=GetFeature&amp;TYPENAMES=geoassets:feature&amp;COUNT=500&amp;OUTPUTFORMAT=application/json
/// GET /wfs?SERVICE=WFS&amp;VERSION=2.0.0&amp;REQUEST=GetFeature&amp;TYPENAMES=geoassets:feature&amp;BBOX=-70,-33,-65,-28,urn:ogc:def:crs:EPSG::4326
/// GET /wfs?SERVICE=WFS&amp;VERSION=2.0.0&amp;REQUEST=DescribeFeatureType&amp;TYPENAMES=geoassets:feature
/// </code>
///
/// Every operation here is read-only (no WFS-T/transactional support), so the whole
/// endpoint requires only <c>features:read</c> (XD01-15) — external GIS clients (QGIS etc.)
/// authenticate the same way any other caller does: a bearer token on the request.
/// </summary>
public static class WfsEndpointExtensions
{
    // OGC namespace URIs used in the capabilities document.
    private static readonly XNamespace _wfs = "http://www.opengis.net/wfs/2.0";
    private static readonly XNamespace _ows = "http://www.opengis.net/ows/1.1";
    private static readonly XNamespace _xlink = "http://www.w3.org/1999/xlink";

    /// <summary>The most features one GetFeature returns, COUNT absent or larger — without BBOX (XD01-449) and with it (XD01-478).</summary>
    public const int MaxFeaturesPerPage = 10_000;

    /// <summary>
    /// The most features a GetFeature counts, with BBOX (XD01-478) and without it (XD01-480): beyond it,
    /// <c>numberMatched</c> is <c>"unknown"</c> rather than the store counting a whole large box or catalog.
    /// </summary>
    public const int MaxNumberMatched = 100_000;

    private const string FeatureTypePrefix = "geoassets:feature:";

    private static readonly JsonSerializerOptions _geoOpts = GeoJsonSerializer.CompactOptions;
    private static readonly JsonSerializerOptions _schemaOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapWfsApi(
        this IEndpointRouteBuilder routes,
        string route = "/wfs")
    {
        routes.MapGet(route, async (HttpRequest req, IAssetProvider provider, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            // WFS uses case-insensitive query params (KVP binding)
            var qs = req.Query;
            string Param(string key) =>
                qs.TryGetValue(key, out var v) ? (v.ToString() ?? string.Empty)
                : qs.TryGetValue(key.ToUpperInvariant(), out v) ? (v.ToString() ?? string.Empty)
                : qs.TryGetValue(key.ToLowerInvariant(), out v) ? (v.ToString() ?? string.Empty)
                : string.Empty;

            var operation = Param("REQUEST");

            return operation.ToUpperInvariant() switch
            {
                "GETCAPABILITIES"     => HandleGetCapabilities(req, provider),
                "GETFEATURE"          => await HandleGetFeatureAsync(Param, provider, timeProvider, cancellationToken).ConfigureAwait(false),
                "DESCRIBEFEATURETYPE" => HandleDescribeFeatureType(),
                _ => Results.BadRequest(
                    $"Unsupported or missing REQUEST parameter: '{operation}'. " +
                    "Supported: GetCapabilities, GetFeature, DescribeFeatureType.")
            };
        })
            .RequireAuthorization("features:read");

        return routes;
    }

    // ── GetCapabilities ───────────────────────────────────────────────────────

    private static IResult HandleGetCapabilities(HttpRequest req, IAssetProvider provider)
    {
        var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}/wfs";
        var xml     = BuildCapabilitiesXml(baseUrl, provider);
        return Results.Content(xml, "application/xml; charset=utf-8");
    }

    private static string BuildCapabilitiesXml(string wfsUrl, IAssetProvider provider)
    {
        var assetTypes = provider.GetAssetTypes();

        // Feature type list: one root type + one per asset type
        var featureTypeElements = new List<XElement>
        {
            BuildFeatureTypeElement(
                name:  "geoassets:feature",
                title: "GeoAssets Features",
                desc:  "All features stored in this GeoAssets instance")
        };
        featureTypeElements.AddRange(assetTypes.Select(t =>
            BuildFeatureTypeElement(
                name:  $"geoassets:feature:{t.Id}",
                title: t.Name,
                desc:  $"Features of type '{t.Name}'")));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(_wfs + "WFS_Capabilities",
                new XAttribute("version", "2.0.0"),
                new XAttribute(XNamespace.Xmlns + "wfs",   _wfs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ows",   _ows.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xlink", _xlink.NamespaceName),

                // ── ServiceIdentification ────────────────────────────────────
                new XElement(_ows + "ServiceIdentification",
                    new XElement(_ows + "Title",             "GeoAssets WFS"),
                    new XElement(_ows + "Abstract",          "OGC WFS 2.0 interface backed by PostGIS"),
                    new XElement(_ows + "ServiceType",       "WFS"),
                    new XElement(_ows + "ServiceTypeVersion","2.0.0")),

                // ── OperationsMetadata ───────────────────────────────────────
                new XElement(_ows + "OperationsMetadata",
                    BuildOperation("GetCapabilities",     wfsUrl),
                    BuildOperation("DescribeFeatureType", wfsUrl),
                    BuildOperation("GetFeature",          wfsUrl,
                        new XElement(_ows + "Parameter",
                            new XAttribute("name", "outputFormat"),
                            new XElement(_ows + "AllowedValues",
                                new XElement(_ows + "Value", "application/json")),
                            new XElement(_ows + "DefaultValue", "application/json")))),

                // ── FeatureTypeList ──────────────────────────────────────────
                new XElement(_wfs + "FeatureTypeList",
                    featureTypeElements)));

        var sb = new StringBuilder();
        using var writer = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8
        });
        doc.WriteTo(writer);
        writer.Flush();
        return sb.ToString();
    }

    private static XElement BuildOperation(string name, string href, params XElement[] extra)
    {
        var op = new XElement(_ows + "Operation",
            new XAttribute("name", name),
            new XElement(_ows + "DCP",
                new XElement(_ows + "HTTP",
                    new XElement(_ows + "Get",
                        new XAttribute(_xlink + "href", href)))));
        foreach (var e in extra) op.Add(e);
        return op;
    }

    private static XElement BuildFeatureTypeElement(string name, string title, string desc) =>
        new(_wfs + "FeatureType",
            new XElement(_wfs + "Name",       name),
            new XElement(_wfs + "Title",      title),
            new XElement(_wfs + "Abstract",   desc),
            new XElement(_wfs + "DefaultCRS", "urn:ogc:def:crs:EPSG::4326"),
            new XElement(_ows + "WGS84BoundingBox",
                new XElement(_ows + "LowerCorner", "-180 -90"),
                new XElement(_ows + "UpperCorner",  "180 90")));

    // ── GetFeature ────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetFeatureAsync(
        Func<string, string> param,
        IAssetProvider       provider,
        TimeProvider         timeProvider,
        CancellationToken    cancellationToken)
    {
        // TYPENAMES (WFS 2.0) / TYPENAME (WFS 1.x legacy)
        var typeName = param("TYPENAMES") is { Length: > 0 } tn ? tn
                     : param("TYPENAME");

        var count      = int.TryParse(param("COUNT"),      CultureInfo.InvariantCulture, out var c)  ? c  : int.MaxValue;
        var startIndex = int.TryParse(param("STARTINDEX"), CultureInfo.InvariantCulture, out var si) ? si : 0;
        var bboxRaw    = param("BBOX");

        if (string.IsNullOrEmpty(bboxRaw))
            return await HandleGetFeaturePageAsync(typeName, count, startIndex, bounds: null, provider, timeProvider, cancellationToken).ConfigureAwait(false);

        // BBOX: minLon,minLat,maxLon,maxLat[,srsName]  (lon/lat, EPSG:4326)
        var parts = bboxRaw.Split(',');
        if (parts.Length < 4 ||
            !double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var minLon) ||
            !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var minLat) ||
            !double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var maxLon) ||
            !double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var maxLat))
        {
            return Results.BadRequest("Invalid BBOX. Expected: minLon,minLat,maxLon,maxLat[,srsName]");
        }

        var bounds = new BoundingBox(minLon, minLat, maxLon, maxLat);
        return await HandleGetFeaturePageAsync(typeName, count, startIndex, bounds, provider, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GetFeature as one page in the catalog's store (<see cref="IAssetProvider.GetPageAsync(AssetQuery, CancellationToken)"/>),
    /// without BBOX (XD01-449) or with it (XD01-478, <see cref="AssetQuery.Bounds"/>): of the type encoded in TYPENAMES
    /// (<c>geoassets:feature:{assetTypeId}</c>) or of every type, at most <see cref="MaxFeaturesPerPage"/> features (COUNT
    /// absent or larger) from STARTINDEX — never the whole catalog or box in memory. <c>numberMatched</c> is the store's
    /// count, exact up to <see cref="MaxNumberMatched"/> and <c>"unknown"</c> (WFS 2.0) beyond, with or without BBOX
    /// (XD01-480), so neither a box nor a large catalog is counted whole. The request's abort token reaches the store's query.
    /// </summary>
    private static async Task<IResult> HandleGetFeaturePageAsync(
        string typeName, int count, int startIndex, BoundingBox? bounds, IAssetProvider provider, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var assetTypeId = !string.IsNullOrEmpty(typeName) && typeName.StartsWith(FeatureTypePrefix, StringComparison.OrdinalIgnoreCase)
            ? typeName[FeatureTypePrefix.Length..]
            : null;

        var page = await provider.GetPageAsync(new AssetQuery
        {
            AssetTypeId = assetTypeId,
            Bounds = bounds,
            CountLimit = MaxNumberMatched,
            Skip = Math.Max(0, startIndex),
            Take = Math.Clamp(count, 0, MaxFeaturesPerPage),
        }, cancellationToken).ConfigureAwait(false);

        var response = new WfsFeatureCollection(
            NumberMatched:  page.TotalCount > MaxNumberMatched ? null : page.TotalCount,
            NumberReturned: page.Items.Count,
            TimeStamp:      timeProvider.GetUtcNow().UtcDateTime.ToString("O"),
            Features:       page.Items);

        return Results.Json(response, _geoOpts, contentType: "application/json");
    }

    // ── DescribeFeatureType ───────────────────────────────────────────────────

    private static IResult HandleDescribeFeatureType()
    {
        // Minimal JSON Schema describing our GeoFeature shape.
        var schema = new
        {
            schema          = "http://json-schema.org/draft-07/schema#",
            title           = "GeoFeature",
            description     = "An OGC GeoJSON feature stored in GeoAssets",
            type            = "object",
            required        = new[] { "type", "id", "geometry", "properties" },
            properties      = new
            {
                type       = new { type = "string", @const = "Feature" },
                id         = new { type = "string", format = "uuid"   },
                geometry   = new { description = "RFC 7946 GeoJSON Geometry" },
                properties = new
                {
                    type       = "object",
                    required   = new[] { "name", "assetTypeId" },
                    properties = new
                    {
                        name             = new { type = "string" },
                        assetTypeId      = new { type = "string", format = "uuid" },
                        description      = new { type = "string" },
                        layerId          = new { type = "string" },
                        srid             = new { type = "integer", @default = 4326 },
                        createdAt        = new { type = "string", format = "date-time" },
                        updatedAt        = new { type = "string", format = "date-time" },
                        customAttributes = new { type = "object", additionalProperties = new { type = "string" } }
                    }
                },
                topology = new
                {
                    type  = "array",
                    items = new { description = "Directed topology edge (TopoEdge)" }
                }
            }
        };

        return Results.Json(schema, _schemaOpts, contentType: "application/schema+json");
    }

    // ── WFS GeoJSON response wrapper ──────────────────────────────────────────

    /// <summary>
    /// OGC WFS 2.0 GeoJSON FeatureCollection response envelope.
    /// Extends the RFC 7946 FeatureCollection with <c>numberMatched</c>,
    /// <c>numberReturned</c>, and <c>timeStamp</c> as required by the WFS spec;
    /// a <c>null</c> <paramref name="NumberMatched"/> is written as WFS 2.0's <c>"unknown"</c>.
    /// </summary>
    private sealed class WfsFeatureCollection(
        int?                     NumberMatched,
        int                      NumberReturned,
        string                   TimeStamp,
        IReadOnlyList<GeoFeature> Features)
    {
        [JsonPropertyName("type")]           public string                   Type           => "FeatureCollection";
        [JsonPropertyName("numberMatched")]  public object                   NumberMatchedV => NumberMatched is { } matched ? matched : "unknown";
        [JsonPropertyName("numberReturned")] public int                      NumberReturnedV => NumberReturned;
        [JsonPropertyName("timeStamp")]      public string                   TimeStampV     => TimeStamp;
        [JsonPropertyName("features")]       public IReadOnlyList<GeoFeature> FeaturesV     => Features;
    }
}
