using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Services;

/// <summary>
/// Singleton that collects every <see cref="IProviderPlugin"/> registered in DI
/// and exposes them for the boot dialog and pool panel. Plugin ids are unique,
/// case-insensitively: a second plugin with an id already registered fails the
/// construction (ADR-002 D7) rather than being silently shadowed.
/// </summary>
public sealed class ProviderPluginRegistry
{
    private readonly Dictionary<string, IProviderPlugin> _byId;

    /// <summary>Every registered plugin, in registration order.</summary>
    public IReadOnlyList<IProviderPlugin> All { get; }

    /// <exception cref="InvalidOperationException">Two plugins share an <see cref="IProviderPlugin.Id"/> (case-insensitive).</exception>
    public ProviderPluginRegistry(IEnumerable<IProviderPlugin> plugins)
    {
        All = [.. plugins];
        _byId = new Dictionary<string, IProviderPlugin>(All.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in All)
        {
            if (!_byId.TryAdd(plugin.Id, plugin))
            {
                var existing = _byId[plugin.Id];
                throw new InvalidOperationException(
                    $"Duplicate provider plugin id '{plugin.Id}': {plugin.GetType().FullName} conflicts with " +
                    $"{existing.GetType().FullName} (id '{existing.Id}'), already registered as IProviderPlugin. " +
                    "Plugin ids must be unique (case-insensitive); remove the duplicate registration or give the plugin another id.");
            }
        }
    }

    /// <summary>The plugin whose <see cref="IProviderPlugin.Id"/> matches <paramref name="id"/> case-insensitively, else <see langword="null"/>.</summary>
    public IProviderPlugin? Find(string id) =>
        _byId.GetValueOrDefault(id);
}
