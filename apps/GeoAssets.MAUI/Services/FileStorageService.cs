using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// IStorageService implementation for MAUI (Android, iOS, Windows, macOS).
/// Uses System.IO with FileSystem.AppDataDirectory.
/// </summary>
public sealed class FileStorageService : IStorageService
{
    private static string GetFilePath(string key) =>
        Path.Combine(FileSystem.AppDataDirectory, $"{key}.geojson");

    public async Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path))
            return new GeoFeatureCollection();

        var json = await File.ReadAllTextAsync(path, ct);
        return GeoJsonSerializer.Deserialize(json) ?? new GeoFeatureCollection();
    }

    public async Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default)
    {
        var path = GetFilePath(key);
        var json = GeoJsonSerializer.Serialize(collection);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public Task<GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default)
    {
        var collection = GeoJsonSerializer.Deserialize(geoJson) ?? new GeoFeatureCollection();
        return Task.FromResult(collection);
    }

    public Task<string> ExportToStringAsync(GeoFeatureCollection collection, CancellationToken ct = default) =>
        Task.FromResult(GeoJsonSerializer.Serialize(collection));

    public async Task<string?> PickImportFileAsync(CancellationToken ct = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Seleccionar archivo GeoJSON",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS,     ["public.json", "public.data"] },
                { DevicePlatform.Android, ["application/json", "application/geo+json"] },
                { DevicePlatform.WinUI,   [".geojson", ".json"] },
                { DevicePlatform.macOS,   ["json", "geojson"] }
            })
        });

        if (result is null) return null;

        using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default)
    {
        // On MAUI, save to the documents folder and notify the user
        var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = Path.Combine(docsPath, suggestedName);
        await File.WriteAllTextAsync(filePath, geoJson, ct);
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, $"{key}.txt");
        if (!File.Exists(path)) return null;
        var value = await File.ReadAllTextAsync(path, ct);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public Task SetStringAsync(string key, string value, CancellationToken ct = default)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, $"{key}.txt");
        return File.WriteAllTextAsync(path, value, ct);
    }
}
