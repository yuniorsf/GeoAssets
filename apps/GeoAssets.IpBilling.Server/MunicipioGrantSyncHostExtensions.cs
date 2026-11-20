namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Registers the município grant-sync dispatcher (XD01-395): <see cref="MunicipioGrantSyncOptions"/> from the
/// <c>GrantSync</c> section, validated on start, its <see cref="MunicipioGrantSyncMetrics"/> and the
/// <see cref="MunicipioGrantSyncDispatcher"/> and <see cref="MunicipioGrantSyncPruner"/> (XD01-400) hosted services. The
/// outbox they drain and prune is <c>AddIpBillingPersistence</c>'s; the <see cref="MunicipioProvisioningService"/> the
/// dispatcher calls and its kernel client are the host's.
/// </summary>
internal static class MunicipioGrantSyncHostExtensions
{
    public static IServiceCollection AddMunicipioGrantSync(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MunicipioGrantSyncOptions>()
            .Bind(configuration.GetSection(MunicipioGrantSyncOptions.SectionName))
            .Validate(MunicipioGrantSyncOptions.IsValid, MunicipioGrantSyncOptions.ValidationMessage)
            .ValidateOnStart();
        services.AddMetrics();
        services.AddSingleton<MunicipioGrantSyncMetrics>();
        services.AddHostedService<MunicipioGrantSyncDispatcher>();
        services.AddHostedService<MunicipioGrantSyncPruner>();

        return services;
    }
}
