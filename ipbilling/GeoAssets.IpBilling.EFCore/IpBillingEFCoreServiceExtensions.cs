using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>DI registration helpers for the GeoAssets IP-billing (GEOS_FAT_IP) persistence layer.</summary>
public static class IpBillingEFCoreServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IpBillingDbContext"/> and the EF Core implementations of every
    /// IP-billing reference-data repository.
    ///
    /// <paramref name="configureDb"/> should set the database provider, e.g.:
    /// <code>
    ///   services.AddIpBillingPersistence(o => o.UseNpgsql(connectionString,
    ///       npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
    /// </code>
    /// </summary>
    public static IServiceCollection AddIpBillingPersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<IpBillingDbContext>(configureDb);

        services.AddScoped<ILampTypeRepository, EFLampTypeRepository>();
        services.AddScoped<IMunicipioRepository, EFMunicipioRepository>();
        services.AddScoped<IUnidadeOrganizacionalRepository, EFUnidadeOrganizacionalRepository>();
        services.AddScoped<IIpTransformerIndexRepository, EFIpTransformerIndexRepository>();
        services.AddScoped<IFaturamentoRepository, EFFaturamentoRepository>();

        return services;
    }
}
