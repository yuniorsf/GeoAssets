using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Abstracts persistence of the GeoFeatureCollection.
/// MAUI: System.IO file on device.
/// Web:  LocalStorage or File System Access API in browser.
/// </summary>
public interface IStorageService
{
    /// <summary>Load the primary feature collection. Returns empty collection if none exists.</summary>
    Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default);

    /// <summary>Persist the feature collection.</summary>
    Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default);

    /// <summary>Parse a GeoJSON string into a FeatureCollection.</summary>
    Task<GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default);

    /// <summary>Serialize the collection as RFC-7946 compliant GeoJSON string.</summary>
    Task<string> ExportToStringAsync(GeoFeatureCollection collection, CancellationToken ct = default);

    /// <summary>Present a file-open dialog and return raw GeoJSON content (null if cancelled).</summary>
    Task<string?> PickImportFileAsync(CancellationToken ct = default);

    /// <summary>Present a file-save dialog and write GeoJSON.</summary>
    Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default);

    /// <summary>Reads a raw string value from persistent storage. Returns null if not found.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);

    /// <summary>Writes a raw string value to persistent storage.</summary>
    Task SetStringAsync(string key, string value, CancellationToken ct = default);
}
