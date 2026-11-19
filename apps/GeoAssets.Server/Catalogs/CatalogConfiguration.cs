namespace GeoAssets.Server.Catalogs;

/// <summary>
/// Reads the server's named asset catalogs from <see cref="ProvidersKey"/> (XD01-371): an array of
/// <c>{ "Id": …, "Plugin": …, "Settings": { … } }</c>, built at startup into the <see cref="CatalogPool"/>.
/// </summary>
public static class CatalogConfiguration
{
    /// <summary>Configuration key of the catalog array.</summary>
    public const string ProvidersKey = "GeoAssets:Providers";

    /// <summary>The catalog a request's <c>IAssetProvider</c> resolves to.</summary>
    public const string DefaultCatalogId = "primary";

    /// <summary>
    /// The configured catalogs, in order. An entry whose <c>Id</c> and <c>Plugin</c> are both blank is ignored, so an
    /// environment variable can switch one off.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An entry has only one of <c>Id</c>/<c>Plugin</c>, two entries share an id, or catalogs are configured but none is
    /// <see cref="DefaultCatalogId"/>.
    /// </exception>
    public static IReadOnlyList<CatalogDefinition> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var definitions = new List<CatalogDefinition>();
        foreach (var entry in configuration.GetSection(ProvidersKey).GetChildren())
        {
            var id = entry["Id"]?.Trim() ?? string.Empty;
            var plugin = entry["Plugin"]?.Trim() ?? string.Empty;
            if (id.Length == 0 && plugin.Length == 0) continue;

            if (id.Length == 0 || plugin.Length == 0)
                throw new InvalidOperationException($"{ProvidersKey}:{entry.Key} needs both an 'Id' and a 'Plugin'.");
            if (definitions.Exists(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"{ProvidersKey} defines catalog '{id}' more than once.");

            var settings = entry.GetSection("Settings").GetChildren()
                .Where(setting => setting.Value is not null)
                .ToDictionary(setting => setting.Key, setting => setting.Value!, StringComparer.OrdinalIgnoreCase);
            definitions.Add(new CatalogDefinition(id, plugin, settings));
        }

        if (definitions.Count > 0 && !definitions.Exists(d => string.Equals(d.Id, DefaultCatalogId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{ProvidersKey} defines catalogs but none with id '{DefaultCatalogId}', the default catalog.");

        return definitions;
    }
}
