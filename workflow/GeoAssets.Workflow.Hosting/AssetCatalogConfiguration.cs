namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The id of the asset catalog instance this host serves (XD01-357): the catalog asset mutation batches are addressed to
/// and the one this host applies them to itself when it has no broker. A kernel setting — not an occupancy one — so the
/// in-process relay and any module staging batches resolve the same value from one place.
/// </summary>
public static partial class AssetCatalogConfiguration
{
    /// <summary>Configuration key for the catalog instance id.</summary>
    public const string InstanceIdKey = "GeoAssets:Catalog:InstanceId";

    /// <summary>
    /// Deprecated key read when <see cref="InstanceIdKey"/> is absent or blank, for hosts configured before XD01-357
    /// (it was the pole-occupancy commit's setting).
    /// </summary>
    public const string LegacyInstanceIdKey = "Workflow:Occupancy:CatalogInstanceId";

    /// <summary>The catalog instance id when neither key is set.</summary>
    public const string DefaultInstanceId = "default";

    /// <summary>
    /// <see cref="InstanceIdKey"/>, trimmed; else <see cref="LegacyInstanceIdKey"/>, trimmed (<paramref name="fromLegacyKey"/>
    /// is then <see langword="true"/>); else <see cref="DefaultInstanceId"/>.
    /// </summary>
    public static string InstanceId(IConfiguration configuration, out bool fromLegacyKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        fromLegacyKey = false;
        var configured = configuration[InstanceIdKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var legacy = configuration[LegacyInstanceIdKey];
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            fromLegacyKey = true;
            return legacy.Trim();
        }

        return DefaultInstanceId;
    }

    /// <inheritdoc cref="InstanceId(IConfiguration, out bool)"/>
    public static string InstanceId(IConfiguration configuration) => InstanceId(configuration, out _);

    /// <summary>Warns that the host is configured through <see cref="LegacyInstanceIdKey"/>.</summary>
    [LoggerMessage(
        EventId = 3570,
        Level = LogLevel.Warning,
        Message = "Configuration key '" + LegacyInstanceIdKey + "' is deprecated; set '" + InstanceIdKey + "' to the catalog instance id '{CatalogInstanceId}' instead.")]
    public static partial void LegacyInstanceIdKeyUsed(ILogger logger, string catalogInstanceId);
}
