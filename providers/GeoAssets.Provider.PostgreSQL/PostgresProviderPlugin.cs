using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// The PostgreSQL/PostGIS asset store as an <see cref="IProviderPlugin"/> (XD01-371): what a server's catalog pool builds
/// a catalog with when the catalog's plugin is <c>"postgres"</c>. The database is named by a connection string of the
/// host's configuration (<see cref="ConnectionStringNameKey"/>, default <see cref="DefaultConnectionStringName"/>) or
/// given inline (<see cref="ConnectionStringKey"/>). The provider is migrated and, unless <see cref="WarmUpKey"/> is
/// <c>false</c>, pre-warmed before it is returned, so its synchronous reads never block on a first load (XD01-173).
/// </summary>
public sealed class PostgresProviderPlugin(PostgresProviderFactory factory) : IProviderPlugin
{
    /// <summary>The plugin id a catalog names to be built by this plugin.</summary>
    public const string PluginId = "postgres";

    /// <summary>Setting: the name of the host connection string (<c>ConnectionStrings:{name}</c>) to use.</summary>
    public const string ConnectionStringNameKey = "connectionStringName";

    /// <summary>Setting: an inline connection string, used instead of <see cref="ConnectionStringNameKey"/>.</summary>
    public const string ConnectionStringKey = "connectionString";

    /// <summary>Setting: <c>true</c> (default) or <c>false</c> — whether the cache is loaded before the catalog serves.</summary>
    public const string WarmUpKey = "warmUp";

    /// <summary>The host connection string used when neither connection setting is given.</summary>
    public const string DefaultConnectionStringName = "GeoAssets";

    public string Id => PluginId;
    public string DisplayName => "PostgreSQL / PostGIS";
    public string Description => "The server's PostgreSQL/PostGIS asset store";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new(ConnectionStringNameKey,
            Label: "Connection string name",
            Placeholder: DefaultConnectionStringName,
            DefaultValue: DefaultConnectionStringName),
        new(ConnectionStringKey,
            Label: "Connection string",
            Type: ProviderFieldType.Password),
        new(WarmUpKey,
            Label: "Warm up the cache at startup",
            Type: ProviderFieldType.Select,
            DefaultValue: "true",
            Options: ["true", "false"]),
    ];

    public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = ConnectionString(config, services);
        return factory.CreateAsync(connectionString, WarmUp(config), ct);
    }

    private static string ConnectionString(ProviderConfig config, IServiceProvider services)
    {
        if (config.Has(ConnectionStringKey)) return config.Get(ConnectionStringKey).Trim();

        var name = config.Has(ConnectionStringNameKey) ? config.Get(ConnectionStringNameKey).Trim() : DefaultConnectionStringName;
        var configuration = services.GetService<IConfiguration>()
            ?? throw new InvalidOperationException(
                $"The '{PluginId}' plugin needs the host configuration to read connection string '{name}'.");

        var connectionString = configuration.GetConnectionString(name);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The '{PluginId}' plugin needs connection string '{name}'. " +
                $"Set ConnectionStrings:{name}, or the catalog's '{ConnectionStringKey}' setting.")
            : connectionString;
    }

    private static bool WarmUp(ProviderConfig config)
    {
        if (!config.Has(WarmUpKey)) return true;

        var value = config.Get(WarmUpKey).Trim();
        return bool.TryParse(value, out var warmUp)
            ? warmUp
            : throw new InvalidOperationException($"The '{PluginId}' plugin's '{WarmUpKey}' setting must be 'true' or 'false', not '{value}'.");
    }
}
