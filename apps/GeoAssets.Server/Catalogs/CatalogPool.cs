using System.Diagnostics.CodeAnalysis;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;

namespace GeoAssets.Server.Catalogs;

/// <summary>
/// The kernel's <see cref="ICatalogPool"/> (XD01-371): the catalogs of <see cref="CatalogConfiguration"/>, each built by
/// the registered <see cref="IProviderPlugin"/> its definition names and wrapped in <see cref="ValidatingAssetProvider"/>.
/// Built once, after the host is built (<see cref="InitializeAsync"/>), so plugins resolve their dependencies from the
/// real container. A catalog whose plugin no enabled module registered is skipped with a warning — with every module
/// off the kernel still boots (invariant I5). The pool owns its catalogs and disposes them.
/// </summary>
public sealed partial class CatalogPool(
    IReadOnlyList<CatalogDefinition> definitions, ProviderPluginRegistry plugins, ILogger<CatalogPool> logger)
    : ICatalogPool, IAsyncDisposable
{
    private Dictionary<string, IAssetProvider>? _catalogs;
    private string[] _catalogIds = [];
    private int _disposed;

    public string DefaultCatalogId => CatalogConfiguration.DefaultCatalogId;

    public IReadOnlyList<string> CatalogIds => _catalogIds;

    public IAssetProvider DefaultCatalog => GetCatalog(DefaultCatalogId);

    public IAssetProvider GetCatalog(string catalogId) =>
        TryGetCatalog(catalogId, out var catalog)
            ? catalog
            : throw new KeyNotFoundException($"No asset catalog '{catalogId}' is configured ({CatalogConfiguration.ProvidersKey}).");

    public bool TryGetCatalog(string catalogId, [NotNullWhen(true)] out IAssetProvider? catalog)
    {
        ArgumentNullException.ThrowIfNull(catalogId);
        return Catalogs.TryGetValue(catalogId, out catalog);
    }

    public TCapability? GetCapability<TCapability>(string catalogId) where TCapability : class
    {
        var catalog = GetCatalog(catalogId);
        return catalog as TCapability ?? catalog.UnwrapToConcrete() as TCapability;
    }

    /// <summary>Builds every configured catalog. Call once, after the host is built and before it serves.</summary>
    /// <exception cref="InvalidOperationException">The pool is already initialized, or a plugin failed to build its catalog.</exception>
    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (_catalogs is not null) throw new InvalidOperationException("The catalog pool is already initialized.");

        var catalogs = new Dictionary<string, IAssetProvider>(StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>();
        try
        {
            foreach (var definition in definitions)
            {
                if (plugins.Find(definition.PluginId) is not { } plugin)
                {
                    LogPluginNotRegistered(logger, definition.Id, definition.PluginId);
                    continue;
                }

                var provider = await plugin
                    .CreateAsync(new ProviderConfig(new Dictionary<string, string>(definition.Settings, StringComparer.OrdinalIgnoreCase)), services, cancellationToken)
                    .ConfigureAwait(false);
                catalogs.Add(definition.Id, new ValidatingAssetProvider(provider));
                ids.Add(definition.Id);
                LogCatalogReady(logger, definition.Id, definition.PluginId);
            }
        }
        catch
        {
            await DisposeAllAsync(catalogs.Values).ConfigureAwait(false);
            throw;
        }

        _catalogIds = [.. ids];
        _catalogs = catalogs;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || _catalogs is null) return;
        await DisposeAllAsync(_catalogs.Values).ConfigureAwait(false);
    }

    private Dictionary<string, IAssetProvider> Catalogs => _catalogs
        ?? throw new InvalidOperationException(
            "The catalog pool is not initialized: call InitializeCatalogPoolAsync after building the host.");

    private static async Task DisposeAllAsync(IEnumerable<IAssetProvider> catalogs)
    {
        foreach (var catalog in catalogs)
        {
            if (catalog is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 3710, Level = LogLevel.Warning,
        Message = "Asset catalog '{CatalogId}' is skipped: no enabled module registers provider plugin '{PluginId}'")]
    private static partial void LogPluginNotRegistered(ILogger logger, string catalogId, string pluginId);

    [LoggerMessage(EventId = 3711, Level = LogLevel.Information,
        Message = "Asset catalog '{CatalogId}' is ready (provider plugin '{PluginId}')")]
    private static partial void LogCatalogReady(ILogger logger, string catalogId, string pluginId);
}
