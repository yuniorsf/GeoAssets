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

    /// <summary>
    /// Credential-free subset of <see cref="All"/> suitable for persistence as reconnect config
    /// (e.g. <see cref="ProjectProviderEntry.Values"/>) — strips file-content keys (<c>*_content</c>)
    /// and any key backing a <see cref="ProviderFieldType.Password"/> field in
    /// <paramref name="fields"/>. The user re-enters those on reconnect.
    /// </summary>
    public Dictionary<string, string> ToReconnectValues(IReadOnlyList<ProviderConfigField> fields)
    {
        var passwordKeys = fields
            .Where(f => f.Type == ProviderFieldType.Password)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, string>(
            _values.Where(kv =>
                !kv.Key.EndsWith("_content", StringComparison.OrdinalIgnoreCase) &&
                !passwordKeys.Contains(kv.Key)),
            StringComparer.OrdinalIgnoreCase);
    }
}
