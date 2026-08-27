using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Components.Layers;

public partial class LayerManager
{
    private readonly Dictionary<Guid, bool> _visibility = [];
    private bool _addingNew;
    private string _newTypeName = string.Empty;
    private string _newTypeColor = "#89b4fa";

    protected override void OnInitialized()
    {
        foreach (var t in Repository.GetAssetTypes())
            _visibility[t.Id] = true;
    }

    private async Task ToggleLayer(AssetType type, bool visible)
    {
        _visibility[type.Id] = visible;
        await MapInterop.SetLayerVisibilityAsync(MapContext.MapDivId, type.Id.ToString(), visible);
    }

    private void SaveNewType()
    {
        if (string.IsNullOrWhiteSpace(_newTypeName)) return;
        var newType = new AssetType { Name = _newTypeName.Trim(), Color = _newTypeColor };
        Repository.AddAssetType(newType);
        _visibility[newType.Id] = true;
        _newTypeName = string.Empty;
        _newTypeColor = "#89b4fa";
        _addingNew = false;
    }

    private void DeleteType(AssetType type)
    {
        Repository.DeleteAssetType(type.Id);
        _visibility.Remove(type.Id);
    }
}
