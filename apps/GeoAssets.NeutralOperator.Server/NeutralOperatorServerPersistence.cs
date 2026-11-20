using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// This host's own workflow store (XD01-382): the generic engine's <see cref="ServiceOrderDbContext"/> on the
/// <c>NeutralOperator</c> connection string (database <c>geoassets_neutral_operator</c>), with its migrations in this
/// assembly (chain starting at <c>InitialNeutralOperatorIsolation</c>) and the tenant interceptors that stamp each
/// connection and transaction with the request's organization for row-level security — and over it the repository stack,
/// target store and outbox the module and the chassis routes need.
/// </summary>
internal static class NeutralOperatorServerPersistence
{
    public const string ConnectionStringName = "NeutralOperator";

    public const string MigrationsAssembly = "GeoAssets.NeutralOperator.Server";

    public static IServiceCollection AddNeutralOperatorServerWorkflowPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddScoped<TenantSessionInterceptor>();
        services.AddScoped<TenantTransactionInterceptor>();

        services.AddWorkflowPersistence(options => options.UseNpgsql(
            connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly)));
        services.ConfigureDbContext<ServiceOrderDbContext>((sp, options) => options.AddInterceptors(
            sp.GetRequiredService<TenantSessionInterceptor>(),
            sp.GetRequiredService<TenantTransactionInterceptor>()));

        return services;
    }
}
