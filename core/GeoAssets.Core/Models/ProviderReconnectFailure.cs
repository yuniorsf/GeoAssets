namespace GeoAssets.Core.Models;

/// <summary>
/// One <see cref="ProjectProviderEntry"/> that failed to reconnect during a Project
/// open/discard reconnect pass — see <see cref="Interfaces.IProjectSessionService.LastReconnectFailures"/>.
/// A file-upload-based provider (e.g. Shapefile) always ends up here on reconnect: its binary
/// content is deliberately not persisted (see <c>ProviderConfig.ToReconnectValues</c>'s doc
/// comment), so the plugin has nothing to recreate the provider from until the user re-supplies
/// the file manually.
/// </summary>
public sealed record ProviderReconnectFailure(string Name, string PluginId, string Reason);
