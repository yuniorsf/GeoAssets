using Blazored.LocalStorage;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.JSInterop;

namespace GeoAssets.Web.Services;

/// <summary>
/// IStorageService implementation for Blazor WebAssembly.
/// Uses Blazored.LocalStorage for persistence; JS interop for file dialogs.
/// </summary>
public sealed class WebStorageService : IStorageService
{
    private readonly ILocalStorageService _localStorage;
    private readonly IJSRuntime _js;

    public WebStorageService(ILocalStorageService localStorage, IJSRuntime js)
    {
        _localStorage = localStorage;
        _js = js;
    }

    public async Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default)
    {
        var json = await _localStorage.GetItemAsStringAsync(key, ct);
        if (string.IsNullOrEmpty(json))
            return new GeoFeatureCollection();

        return GeoJsonSerializer.Deserialize(json) ?? new GeoFeatureCollection();
    }

    public async Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default)
    {
        var json = GeoJsonSerializer.Serialize(collection);
        await _localStorage.SetItemAsStringAsync(key, json, ct);
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
        try
        {
            // Try File System Access API (Chrome/Edge)
            return await _js.InvokeAsync<string>("GeoAssets.openGeoJsonFilePicker", ct);
        }
        catch
        {
            // Fallback: caller should use InputFile component instead
            return null;
        }
    }

    public async Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default)
    {
        try
        {
            // Try File System Access API (Chrome/Edge)
            await _js.InvokeVoidAsync("GeoAssets.saveGeoJsonFilePicker", ct, geoJson, suggestedName);
        }
        catch
        {
            // Fallback: trigger download link (Firefox/Safari)
            await _js.InvokeVoidAsync("GeoAssets.downloadAsFile", ct, geoJson, suggestedName);
        }
    }
}
