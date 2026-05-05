using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Provider.InMemory;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// Provider plugin that loads geographic features from an ESRI Shapefile
/// packaged as a ZIP archive (must include .shp and .dbf; .prj optional).
/// Features are imported once into an in-memory collection — edits made in
/// the session are not written back to the original file.
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
        var content = config.Get("archive_content");
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                "Shapefile ZIP archive is required. " +
                "Please select a .zip file containing your .shp and .dbf files.");

        var features = ShapefileParser.ParseZip(content);

        var provider = new InMemoryAssetProvider();
        provider.AddRange(features);

        return Task.FromResult<IAssetProvider>(provider);
    }
}
