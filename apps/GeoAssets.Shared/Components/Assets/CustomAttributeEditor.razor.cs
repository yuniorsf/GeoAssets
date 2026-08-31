using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Assets;

public partial class CustomAttributeEditor
{
    [Parameter] public Dictionary<string, string> Attributes { get; set; } = [];

    private List<string> _keys = [];

    protected override void OnParametersSet()
    {
        _keys = [.. Attributes.Keys];
    }

    private void AddAttribute()
    {
        var key = $"campo_{_keys.Count + 1}";
        Attributes[key] = string.Empty;
        _keys.Add(key);
    }

    private void RenameKey(string oldKey, string newKey)
    {
        if (string.IsNullOrWhiteSpace(newKey) || newKey == oldKey) return;
        var value = Attributes.TryGetValue(oldKey, out var v) ? v : string.Empty;
        Attributes.Remove(oldKey);
        Attributes[newKey] = value;
        var idx = _keys.IndexOf(oldKey);
        if (idx >= 0) _keys[idx] = newKey;
    }

    private void SetValue(string key, string value)
    {
        Attributes[key] = value;
    }

    private void RemoveKey(string key)
    {
        Attributes.Remove(key);
        _keys.Remove(key);
    }
}
