using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Plugin.GeoJsonImport.Tests;

internal sealed class TempGeoJsonFiles : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-geojson-import-" + Guid.NewGuid().ToString("N"));

    public TempGeoJsonFiles() => Directory.CreateDirectory(_directory);

    public string WriteRaw(string content)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".geojson");
        File.WriteAllText(path, content);
        return path;
    }

    public string Write(IEnumerable<GeoFeature> features, IEnumerable<AssetType>? extraAssetTypes = null)
    {
        var collection = new GeoFeatureCollection { Features = [.. features] };
        if (extraAssetTypes is not null)
            collection.Metadata.AssetTypes.AddRange(extraAssetTypes);
        return WriteRaw(GeoJsonSerializer.Serialize(collection));
    }

    public string MissingPath() => Path.Combine(_directory, "missing.geojson");

    public static GeoFeature Feature(string id, string name = "n") => new()
    {
        Id = id,
        Geometry = new GeoPoint(-66.1, 18.4),
        Properties = new GeoFeatureProperties { Name = name },
    };

    public static AssetType CustomType(Guid id, string name = "Custom") => new() { Id = id, Name = name, IsBuiltIn = false };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
