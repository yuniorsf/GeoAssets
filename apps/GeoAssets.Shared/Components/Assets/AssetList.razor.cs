using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Assets;

public partial class AssetList
{
    private List<GeoFeature> _filtered = [];
    private string _searchQuery = string.Empty;
    private string _typeFilter = string.Empty;
    private bool _deleteConfirmVisible;
    private GeoFeature? _pendingDelete;

    protected override void OnInitialized()
    {
        Repository.FeatureAdded   += OnChanged;
        Repository.FeatureUpdated += OnChanged;
        Repository.FeatureDeleted += OnDeleted;
        RefreshList();
    }

    private void OnChanged(object? _, GeoFeature __) { RefreshList(); InvokeAsync(StateHasChanged); }
    private void OnDeleted(object? _, string __)     { RefreshList(); InvokeAsync(StateHasChanged); }

    private void RefreshList()
    {
        var all = string.IsNullOrEmpty(_searchQuery)
            ? Repository.GetAll()
            : Repository.Search(_searchQuery);

        _filtered = string.IsNullOrEmpty(_typeFilter)
            ? [.. all]
            : [.. all.Where(f => f.Properties.AssetTypeId == _typeFilter)];
    }

    private void OnSearch(string query)    { _searchQuery = query;  RefreshList(); }
    private void OnTypeFilter(ChangeEventArgs e) { _typeFilter = e.Value?.ToString() ?? string.Empty; RefreshList(); }

    private void SelectFeature(GeoFeature f) => Selection.Select(f);

    private void RequestDelete(GeoFeature f) { _pendingDelete = f; _deleteConfirmVisible = true; }
    private void CancelDelete()             { _pendingDelete = null; _deleteConfirmVisible = false; }

    private void ConfirmDelete()
    {
        if (_pendingDelete is null) return;
        var wasSelected = Selection.Selected?.Id == _pendingDelete.Id;
        Repository.Delete(_pendingDelete.Id);
        if (wasSelected) Selection.Clear();
        _pendingDelete = null;
        _deleteConfirmVisible = false;
    }

    private string GetColor(string assetTypeId)
    {
        var t = Repository.GetAssetTypes().FirstOrDefault(x => x.Id.ToString() == assetTypeId);
        return t?.Color ?? "#3388ff";
    }

    private static string GeometryIcon(GeoFeature f) => f.Geometry switch
    {
        Core.Models.Geometry.GeoPoint      => "📍",
        Core.Models.Geometry.GeoLineString => "〰️",
        Core.Models.Geometry.GeoPolygon    => "⬡",
        _                                  => "?"
    };

    public override void Dispose()
    {
        Repository.FeatureAdded   -= OnChanged;
        Repository.FeatureUpdated -= OnChanged;
        Repository.FeatureDeleted -= OnDeleted;
        base.Dispose();
    }
}
