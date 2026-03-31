namespace GeoAssets.Core.Models;

/// <summary>
/// Typed key-value bag that carries user-entered configuration from the UI
/// to <see cref="GeoAssets.Core.Interfaces.IProviderPlugin.CreateAsync"/>.
/// Keys are case-insensitive.
/// </summary>
public sealed class ProviderConfig
{
    private readonly Dictionary<string, string> _values;

    public ProviderConfig() => _values = new(StringComparer.OrdinalIgnoreCase);

    public ProviderConfig(IDictionary<string, string> initial) =>
        _values = new(initial, StringComparer.OrdinalIgnoreCase);

    public string Get(string key, string @default = "") =>
        _values.TryGetValue(key, out var v) ? v : @default;

    public void Set(string key, string value) => _values[key] = value;

    public bool Has(string key) => _values.ContainsKey(key) && !string.IsNullOrWhiteSpace(_values[key]);

    public IReadOnlyDictionary<string, string> All => _values;
}
