using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Components.Providers;

public partial class ProviderPoolPanel
{
    private readonly Dictionary<Guid, string> _messages = [];
    private Guid?  _editingId;
    private string _editingName = string.Empty;
    private bool   _showConnectDialog;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnInitialized() => Pool.Changed += OnPoolChanged;

    private void OnPoolChanged(object? _, EventArgs __) =>
        InvokeAsync(StateHasChanged);

    public override void Dispose()
    {
        Pool.Changed -= OnPoolChanged;
        base.Dispose();
    }

    // ── Connect dialog ────────────────────────────────────────────────────────

    private void OpenConnectDialog()  => _showConnectDialog = true;
    private void CloseConnectDialog() => _showConnectDialog = false;

    // ── Show / Hide ───────────────────────────────────────────────────────────

    private async Task ToggleEnabledAsync(ProviderEntry entry)
    {
        if (entry.IsEnabled)
        {
            Pool.Disable(entry.Id);
            if (entry.Provider is IWmsProvider)
                await MapInterop.RemoveWmsLayerAsync(MapContext.MapDivId, entry.Id.ToString());
            else
                foreach (var f in entry.Provider.GetAll())
                    await MapInterop.RemoveFeatureAsync(MapContext.MapDivId, f.Id);
        }
        else
        {
            Pool.Enable(entry.Id);
            if (entry.Provider is IWmsProvider wms)
                await MapInterop.AddWmsLayerAsync(MapContext.MapDivId, entry.Id.ToString(),
                    wms.WmsBaseUrl,
                    new WmsLayerOptions(Layers: wms.WmsLayerName, Format: wms.WmsFormat));
            else
                foreach (var f in entry.Provider.GetAll())
                    await MapInterop.RenderFeatureAsync(MapContext.MapDivId, f);
        }
    }

    // ── Set active workspace ──────────────────────────────────────────────────

    private async Task SetActiveAsync(ProviderEntry entry)
    {
        Pool.SetActive(entry.Id);
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private async Task ExportEntryAsync(ProviderEntry entry)
    {
        _messages[entry.Id] = string.Empty;
        try
        {
            var collection = new GeoFeatureCollection
            {
                Features = [.. entry.Provider.GetAll()],
                Metadata = new GeoFeatureCollectionMetadata
                {
                    Name       = entry.Name,
                    CreatedAt  = Clock.GetUtcNow().UtcDateTime,
                    AssetTypes = [.. entry.Provider.GetAssetTypes()]
                }
            };
            var json     = await Storage.ExportToStringAsync(collection);
            var safeName = entry.Name.ToLowerInvariant().Replace(" ", "-");
            await Storage.SaveExportFileAsync(json, $"{safeName}-export.geojson");

            _messages[entry.Id] = L["import.exportSuccess"];
            Logger.LogInformation("Export — collection: {Name}, features: {Count}",
                entry.Name, collection.Features.Count);
        }
        catch (Exception ex)
        {
            _messages[entry.Id] = L.GetString("import.errorExport", ex.Message);
            Logger.LogError(ex, "Export failed — collection: {Name}", entry.Name);
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async Task ImportFileAsync(InputFileChangeEventArgs ev, ProviderEntry entry)
    {
        _messages[entry.Id] = string.Empty;
        try
        {
            using var stream = ev.File.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024);
            using var reader = new System.IO.StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            var imported = await Storage.ImportFromStringAsync(json);

            foreach (var t in imported.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
                entry.Provider.AddAssetType(t);

            entry.Provider.AddRange(imported.Features);

            if (entry.IsEnabled)
                foreach (var f in imported.Features)
                    await MapInterop.RenderFeatureAsync(MapContext.MapDivId, f);

            _messages[entry.Id] = L.GetString("import.successMsg", ev.File.Name, imported.Features.Count);

            Logger.LogInformation("Import — collection: {Name}, file: {File}, features: {Count}",
                entry.Name, ev.File.Name, imported.Features.Count);

            Analytics.TrackEvent("import.completed", new
            {
                fileName     = ev.File.Name,
                featureCount = imported.Features.Count,
                collection   = entry.Name,
                isActive     = entry.IsActive,
                outcome      = "success"
            });
        }
        catch (Exception ex)
        {
            _messages[entry.Id] = L.GetString("import.errorImport", ex.Message);
            Logger.LogError(ex, "Import failed — collection: {Name}, file: {File}",
                entry.Name, ev.File.Name);
            Analytics.TrackException(ex.Message, new
            {
                fileName   = ev.File.Name,
                collection = entry.Name,
                outcome    = "failure"
            });
        }
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private void StartRename(ProviderEntry entry)
    {
        _editingId   = entry.Id;
        _editingName = entry.Name;
    }

    private void CommitRename(ProviderEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_editingName))
            Pool.Rename(entry.Id, _editingName.Trim());
        _editingId = null;
    }

    private void HandleRenameKey(KeyboardEventArgs ev, ProviderEntry entry)
    {
        if (ev.Key == "Enter")  CommitRename(entry);
        if (ev.Key == "Escape") _editingId = null;
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    private async Task RemoveEntryAsync(ProviderEntry entry)
    {
        if (entry.IsActive) return;
        if (entry.Provider is IWmsProvider)
            await MapInterop.RemoveWmsLayerAsync(MapContext.MapDivId, entry.Id.ToString());
        else
            foreach (var f in entry.Provider.GetAll())
                await MapInterop.RemoveFeatureAsync(MapContext.MapDivId, f.Id);
        _messages.Remove(entry.Id);
        Pool.Remove(entry.Id);
    }
}
