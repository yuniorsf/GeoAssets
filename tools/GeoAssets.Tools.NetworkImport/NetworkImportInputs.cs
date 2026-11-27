using System.Globalization;
using System.Text.RegularExpressions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>Validates option values; an unusable value throws <see cref="NetworkImportInputException"/>.</summary>
internal static partial class NetworkImportInputs
{
    private const string PMTilesExport = "PMTILES";

    public static ISourceProfile Profile(string? profileId)
    {
        var registry = BuiltInProfiles.CreateRegistry();
        try
        {
            return registry.Find(profileId ?? string.Empty);
        }
        catch (KeyNotFoundException exception)
        {
            throw new NetworkImportInputException($"Unknown profile '{profileId}' (known: {string.Join(", ", registry.ProfileIds)}).", exception);
        }
    }

    public static string Root(string? root) =>
        !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)
            ? root
            : throw new NetworkImportInputException($"The delivery folder '{root}' does not exist.");

    public static Guid Organization(string? organization) =>
        Guid.TryParse(organization, out var id) && id != Guid.Empty
            ? id
            : throw new NetworkImportInputException($"--organization must be a non-empty organization id (GUID), not '{organization}'.");

    public static Uri KernelUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
            throw new NetworkImportInputException($"--kernel-url must be an absolute http(s) URL, not '{url}'.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    public static string ImportId(string importId) =>
        ImportIdPattern().IsMatch(importId)
            ? importId
            : throw new NetworkImportInputException(
                $"--import-id must be 1 to {FeatureImportLimits.MaxImportIdLength} letters, digits, '.', '_' or '-', not '{importId}'.");

    public static string? ProjDataDirectory(string? directory) =>
        directory is null || Directory.Exists(directory)
            ? directory
            : throw new NetworkImportInputException($"The PROJ data folder '{directory}' does not exist.");

    public static string FeaturesFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : throw new NetworkImportInputException($"The features file '{path}' does not exist.");

    /// <summary>
    /// The <c>--export</c> vector file format; <see langword="null"/> when none is asked for, or a PMTiles archive is
    /// (<see cref="TileExport"/>).
    /// </summary>
    public static VectorFileFormat? ExportFormat(string? export) => export?.ToUpperInvariant() switch
    {
        null or PMTilesExport => null,
        "FLATGEOBUF" => VectorFileFormat.FlatGeobuf,
        "GEOPARQUET" => VectorFileFormat.GeoParquet,
        _ => throw new NetworkImportInputException($"--export must be flatgeobuf, geoparquet or pmtiles, not '{export}'."),
    };

    /// <summary>
    /// The <c>--export pmtiles</c> archive's zoom range (XD01-472): <c>--min-zoom</c> … <c>--max-zoom</c>, by default
    /// <see cref="VectorTileArchiveWriteRequest.DefaultMinZoom"/> … <see cref="VectorTileArchiveWriteRequest.DefaultMaxZoom"/>;
    /// <see langword="null"/> when no archive is asked for (and then neither option may be given).
    /// </summary>
    public static (int MinZoom, int MaxZoom)? TileExport(string? export, string? minZoom, string? maxZoom)
    {
        if (!string.Equals(export, PMTilesExport, StringComparison.OrdinalIgnoreCase))
        {
            return minZoom is null && maxZoom is null
                ? null
                : throw new NetworkImportInputException("--min-zoom and --max-zoom go with --export pmtiles.");
        }

        var min = Zoom("--min-zoom", minZoom, VectorTileArchiveWriteRequest.DefaultMinZoom);
        var max = Zoom("--max-zoom", maxZoom, VectorTileArchiveWriteRequest.DefaultMaxZoom);
        return min <= max
            ? (min, max)
            : throw new NetworkImportInputException(
                string.Create(CultureInfo.InvariantCulture, $"--min-zoom ({min}) must not be above --max-zoom ({max})."));
    }

    private static int Zoom(string option, string? value, int defaultZoom)
    {
        if (value is null)
            return defaultZoom;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var zoom) && zoom <= VectorTileArchiveWriteRequest.MaxSupportedZoom
            ? zoom
            : throw new NetworkImportInputException(
                string.Create(CultureInfo.InvariantCulture, $"{option} must be a zoom from 0 to {VectorTileArchiveWriteRequest.MaxSupportedZoom}, not '{value}'."));
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ImportIdPattern();
}
