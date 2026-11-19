namespace GeoAssets.Server.Catalogs;

/// <summary>
/// One entry of <see cref="CatalogConfiguration.ProvidersKey"/> (XD01-371): the catalog <paramref name="Id"/>, the
/// <c>IProviderPlugin.Id</c> that builds it, and the plugin's settings (case-insensitive keys).
/// </summary>
public sealed record CatalogDefinition(string Id, string PluginId, IReadOnlyDictionary<string, string> Settings);
