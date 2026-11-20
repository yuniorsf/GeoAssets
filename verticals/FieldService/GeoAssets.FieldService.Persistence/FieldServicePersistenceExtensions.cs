using System.Data.Common;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.FieldService.Infrastructure;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.FieldService.Persistence;

/// <summary>DI registration of the FieldService vertical's persistence (XD01-381).</summary>
public static class FieldServicePersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="FieldServiceDbContext"/> on PostgreSQL over the connection of the scope's kernel
    /// <see cref="ServiceOrderDbContext"/> (which the host must register), and the stores on it — see
    /// <see cref="AddFieldServicePersistence(IServiceCollection, Action{DbContextOptionsBuilder, DbConnection})"/>.
    /// </summary>
    public static IServiceCollection AddFieldServicePersistence(this IServiceCollection services) =>
        services.AddFieldServicePersistence(
            (options, connection) => options.UseNpgsql(connection, FieldServiceDbContext.ConfigureMigrations));

    /// <summary>
    /// Registers the vertical's persistence until Phase 7 gives it a host database of its own:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="FieldServiceDbContext"/>, scoped, built by <paramref name="useProvider"/> over the <b>same</b>
    /// <see cref="DbConnection"/> as the scope's kernel <see cref="ServiceOrderDbContext"/>, joining whatever transaction
    /// that context has open (<see cref="ServiceOrderAmbientTransaction"/>) — so a write in the kernel's unit of work,
    /// order transition or outbox write is part of it. When an <see cref="ITenantContext"/> is registered the context also
    /// stamps that tenant on the connections and transactions it opens itself (<see cref="TenantSessionInterceptor"/>,
    /// <see cref="TenantTransactionInterceptor"/>), as the kernel's does.
    /// </description></item>
    /// <item><description>
    /// The stores, one scoped instance each: <see cref="IContractorUsLedgerRepository"/> (XD01-207),
    /// <see cref="IUsCatalogRepository"/> and <see cref="IContractorUsPostingRepository"/> (XD01-241/XD01-242),
    /// <see cref="IPavingMeasurementRepository"/> (XD01-291) and <see cref="IManholeAuditRepository"/> (XD01-265).
    /// </description></item>
    /// <item><description>
    /// <see cref="InspectionCommitService"/> (XD01-228), scoped: it also needs the kernel's
    /// <see cref="Workflow.Orders.IServiceOrderRepository"/>, <see cref="Core.Messaging.IOutboxWriter"/>,
    /// <see cref="Workflow.Orders.OrderTypeRegistry"/> and <see cref="TimeProvider"/>, which the host registers.
    /// </description></item>
    /// </list>
    /// The kernel's <c>AddWorkflowPersistence</c> registers none of these.
    /// </summary>
    public static IServiceCollection AddFieldServicePersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, DbConnection> useProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(useProvider);

        services.TryAddScoped<IAmbientDbTransaction>(sp => new ServiceOrderAmbientTransaction(sp.GetRequiredService<ServiceOrderDbContext>()));
        services.AddDbContext<FieldServiceDbContext>((sp, options) =>
        {
            useProvider(options, sp.GetRequiredService<ServiceOrderDbContext>().Database.GetDbConnection());
            if (sp.GetService<ITenantContext>() is { } tenant)
                options.AddInterceptors(new TenantSessionInterceptor(tenant), new TenantTransactionInterceptor(tenant));
        });

        services.TryAddScoped<IContractorUsLedgerRepository, EFContractorUsLedgerRepository>();
        services.TryAddScoped<IUsCatalogRepository, EFUsCatalogRepository>();
        services.TryAddScoped<IContractorUsPostingRepository, EFContractorUsPostingRepository>();
        services.TryAddScoped<IPavingMeasurementRepository, EFPavingMeasurementRepository>();
        services.TryAddScoped<IManholeAuditRepository, EFManholeAuditRepository>();
        services.TryAddScoped<InspectionCommitService>();

        return services;
    }
}
