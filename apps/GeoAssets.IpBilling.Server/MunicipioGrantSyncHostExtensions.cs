namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Registers the município grant-sync dispatcher (XD01-395): <see cref="MunicipioGrantSyncOptions"/> from the
/// <c>GrantSync</c> section, validated on start, its <see cref="MunicipioGrantSyncMetrics"/> and the
/// <see cref="MunicipioGrantSyncDispatcher"/> hosted service. The outbox it drains is <c>AddIpBillingPersistence</c>'s; the
/// <see cref="MunicipioProvisioningService"/> it calls and its kernel client are the host's.
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

        return services;
    }
}
