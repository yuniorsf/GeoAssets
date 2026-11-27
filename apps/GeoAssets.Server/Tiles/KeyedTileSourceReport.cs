using System.Runtime.Loader;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Rendering;
using GeoAssets.Hosting.Abstractions.Plugins;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// Reports, once the host has started, what the deployment-wide tile sources serve (XD01-459): each
/// <see cref="KeyedTileSources"/> source describes itself (so a broken file is logged at startup, not first seen by a
/// map), then the provider plugins loaded from the plugin directory are listed with the native libraries their load
/// contexts resolved, and every native library claimed in <see cref="NativeLibraryOwnership"/> with its owner. In the
/// background: a slow remote source never delays the startup. A source that fails is logged and stays out of
/// <c>style.json</c> until it describes itself; nothing here fails the host.
/// </summary>
public sealed partial class KeyedTileSourceReport(
    IServiceProvider services, ILogger<KeyedTileSourceReport> logger, IPluginLoadContextRegistry? plugins = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        foreach (var source in services.GetRequiredService<KeyedTileSources>().ResolveAll(services))
        {
            var format = source.Format.FileExtension();
            try
            {
                var described = await source.Described.DescribeAsync(stoppingToken).ConfigureAwait(false);
                var layers = string.Join(',', described.LayerIds);
                LogServing(logger, format, source.Id, described.Version, layers, described.MinZoom, described.MaxZoom);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogUndescribed(logger, ex, format, source.Id);
            }
        }

        foreach (var plugin in plugins?.Plugins ?? [])
        {
            var natives = plugin.LoadContext.LoadedNativeLibraries.Distinct(StringComparer.Ordinal).ToList();
            var modules = string.Join(',', plugin.Modules.Select(m => m.ModuleType.Name));
            var files = string.Join(", ", natives.Select(path => Path.GetRelativePath(plugin.LoadContext.PluginDirectory, path)));
            var context = plugin.LoadContext.Name ?? plugin.Name;
            LogPlugin(logger, plugin.Name, plugin.LoadContext.PluginDirectory, context, modules, natives.Count, files);
        }

        foreach (var (library, owner) in NativeLibraryOwnership.Claims)
        {
            var name = owner.GetName().Name ?? owner.FullName ?? "?";
            var context = AssemblyLoadContext.GetLoadContext(owner)?.Name ?? "?";
            LogOwner(logger, library, name, context);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Serving the {Format} tile source '{SourceId}' under the version {Version} (layers {Layers}, zooms {MinZoom}-{MaxZoom})")]
    private static partial void LogServing(ILogger logger, string format, string sourceId, string version, string layers, int minZoom, int maxZoom);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Format} tile source '{SourceId}' can't describe itself; it is not listed in style.json until it can")]
    private static partial void LogUndescribed(ILogger logger, Exception ex, string format, string sourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provider plugin '{Plugin}' loaded from {Directory} in load context '{LoadContext}' (module types {Modules}); {NativeCount} distinct native libraries resolved from its folder so far: {Natives}")]
    private static partial void LogPlugin(ILogger logger, string plugin, string directory, string loadContext, string modules, int nativeCount, string natives);

    [LoggerMessage(Level = LogLevel.Information, Message = "Native library '{Library}' is claimed by {Owner} in load context '{LoadContext}'")]
    private static partial void LogOwner(ILogger logger, string library, string owner, string loadContext);
}
