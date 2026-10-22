using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// Provider plugin that loads geographic features from an ESRI Shapefile
/// packaged as a ZIP archive (must include .shp and .dbf; if .prj is present, its CRS is
/// reprojected to WGS84, otherwise coordinates are assumed to already be WGS84).
/// Features are imported once into an in-memory collection — edits made in
/// the session are not written back to the original file.
///
/// On a host with real filesystem access (MAUI, via <c>INativeFilePicker</c> — see
/// <c>ProviderConfigForm</c>), the config additionally carries an <c>archive_path</c> — the
/// archive's real full path on disk, which survives <see cref="ProviderConfig.ToReconnectValues"/>
/// (unlike <c>archive_content</c>, deliberately stripped before persisting) — so
/// <see cref="CreateAsync"/> can reopen the file directly from disk on reconnect instead of
/// requiring the user to re-pick it every time a Project is reopened. On Web there's no such
/// path (a browser file input never exposes one), so reconnect there still requires a re-pick,
/// surfaced the same way any other Shapefile reconnect failure is.
/// </summary>
public sealed class ShapefileProviderPlugin : IProviderPlugin
{
    public string Id          => "shapefile";
    public string DisplayName => "Shapefile (SHP)";
    public string Description => "Import features from an ESRI Shapefile ZIP archive (.shp + .dbf)";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label:        "Collection name",
            Placeholder:  "My shapefile",
            DefaultValue: "Shapefile"),
        new("archive",
            Label:    "Shapefile ZIP archive",
            Type:     ProviderFieldType.BinaryFile,
            Accept:   ".zip",
            Required: true)
    ];

    public Task<IAssetProvider> CreateAsync(
        ProviderConfig    config,
        IServiceProvider  services,
        CancellationToken ct = default)
    {
        var zipBytes = ResolveArchiveBytes(config);

        var features = ShapefileParser.ParseZipBytes(zipBytes);

        var provider = new ShapefileFeatureStore();
        provider.AddRange(features);

        return Task.FromResult<IAssetProvider>(provider);
    }

    private static byte[] ResolveArchiveBytes(ProviderConfig config)
    {
        var content = config.Get("archive_content");
        if (!string.IsNullOrWhiteSpace(content))
            return Convert.FromBase64String(content);

        // No freshly-uploaded content — this is a reconnect. Fall back to the full path saved at
        // connect time (MAUI only; never set on Web), rather than requiring a re-pick every time.
        var path = config.Get("archive_path");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "Shapefile ZIP archive is required. " +
                "Please select a .zip file containing your .shp and .dbf files.");

        if (OperatingSystem.IsBrowser())
            throw new InvalidOperationException(
                $"Can't reopen '{Path.GetFileName(path)}' automatically in the browser — " +
                "please reconnect by re-selecting the file.");

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Shapefile not found at '{path}'. Please reconnect by re-selecting the file.");

        return File.ReadAllBytes(path);
    }
}
