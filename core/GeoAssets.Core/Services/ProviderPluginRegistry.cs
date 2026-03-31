using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Services;

/// <summary>
/// Singleton that collects every <see cref="IProviderPlugin"/> registered in DI
/// and exposes them for the boot dialog and pool panel.
/// </summary>
public sealed class ProviderPluginRegistry
{
    public IReadOnlyList<IProviderPlugin> All { get; }

    public ProviderPluginRegistry(IEnumerable<IProviderPlugin> plugins) =>
        All = [.. plugins];

    public IProviderPlugin? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
