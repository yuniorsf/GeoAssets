using System.Data.Common;
using GeoAssets.Core.Interfaces;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>DI registration of the NeutralOperator vertical's persistence (XD01-380).</summary>
public static class NeutralOperatorPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="NeutralOperatorDbContext"/> on PostgreSQL over the connection of the scope's kernel
    /// <see cref="ServiceOrderDbContext"/> (which the host must register), and the stores on it — see
    /// <see cref="AddNeutralOperatorPersistence(IServiceCollection, Action{DbContextOptionsBuilder, DbConnection})"/>.
    /// </summary>
    public static IServiceCollection AddNeutralOperatorPersistence(this IServiceCollection services) =>
        services.AddNeutralOperatorPersistence(
            (options, connection) => options.UseNpgsql(connection, NeutralOperatorDbContext.ConfigureMigrations));

    /// <summary>
    /// Registers the vertical's persistence until Phase 7 gives it a database of its own:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="NeutralOperatorDbContext"/>, scoped, built by <paramref name="useProvider"/> over the <b>same</b>
    /// <see cref="DbConnection"/> as the scope's kernel <see cref="ServiceOrderDbContext"/>, joining whatever transaction
    /// that context has open (<see cref="ServiceOrderAmbientTransaction"/>) — so an occupancy write in the kernel's unit of
    /// work, order transition or outbox write is part of it. When an <see cref="ITenantContext"/> is registered the context
    /// also stamps that tenant on the connections and transactions it opens itself
    /// (<see cref="TenantSessionInterceptor"/>, <see cref="TenantTransactionInterceptor"/>), as the kernel's does.
    /// </description></item>
    /// <item><description>
    /// Pole occupancy (XD01-314): <see cref="IPoleOccupancyReader"/> and <see cref="IPoleOccupancyWriter"/> on one scoped
    /// <see cref="EFPoleOccupancyRepository"/>.
    /// </description></item>
    /// <item><description>
    /// Staged occupancy changes (XD01-315): <see cref="IOccupancyChangeSetReader"/>, <see cref="IOccupancyChangeSetWriter"/>
    /// and <see cref="IOccupancyChangeSetCloser"/> on one scoped <see cref="EFOccupancyChangeSetRepository"/>, and
    /// <see cref="OccupancyReleaseLifecycleHook"/>, which discards what is still staged on an order when it closes, in the
    /// closing transition's transaction (XD01-362).
    /// </description></item>
    /// </list>
    /// <see cref="TimeProvider"/> is used when registered, the system clock otherwise.
    /// </summary>
    public static IServiceCollection AddNeutralOperatorPersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, DbConnection> useProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(useProvider);

        services.TryAddScoped<IAmbientDbTransaction>(sp => new ServiceOrderAmbientTransaction(sp.GetRequiredService<ServiceOrderDbContext>()));
        services.AddDbContext<NeutralOperatorDbContext>((sp, options) =>
        {
            useProvider(options, sp.GetRequiredService<ServiceOrderDbContext>().Database.GetDbConnection());
            if (sp.GetService<ITenantContext>() is { } tenant)
                options.AddInterceptors(new TenantSessionInterceptor(tenant), new TenantTransactionInterceptor(tenant));
        });

        services.TryAddScoped(sp => new EFPoleOccupancyRepository(
            sp.GetRequiredService<NeutralOperatorDbContext>(),
            sp.GetService<TimeProvider>()));
        services.TryAddScoped<IPoleOccupancyReader>(sp => sp.GetRequiredService<EFPoleOccupancyRepository>());
        services.TryAddScoped<IPoleOccupancyWriter>(sp => sp.GetRequiredService<EFPoleOccupancyRepository>());

        services.TryAddScoped(sp => new EFOccupancyChangeSetRepository(
            sp.GetRequiredService<NeutralOperatorDbContext>(),
            sp.GetService<TimeProvider>()));
        services.TryAddScoped<IOccupancyChangeSetReader>(sp => sp.GetRequiredService<EFOccupancyChangeSetRepository>());
        services.TryAddScoped<IOccupancyChangeSetWriter>(sp => sp.GetRequiredService<EFOccupancyChangeSetRepository>());

        // Closing an order discards what is still staged on it, in the closing transition's transaction (XD01-362).
        services.TryAddScoped<IOccupancyChangeSetCloser>(sp => sp.GetRequiredService<EFOccupancyChangeSetRepository>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrderLifecycleHook, OccupancyReleaseLifecycleHook>());

        return services;
    }

    /// <summary>
    /// Registers what the occupancy approval (XD01-318, <c>AddOccupancyCommit</c>) needs from the persistence layer:
    /// <see cref="EFWorkflowUnitOfWork"/> as <see cref="IWorkflowUnitOfWork"/> on the scope's kernel
    /// <see cref="ServiceOrderDbContext"/> (when not registered yet) — the transaction the occupancy stores join — and
    /// <see cref="ObservableOccupancyCommitService"/> around the concrete <see cref="OccupancyCommitService"/>. Call it after
    /// <c>AddOccupancyCommit()</c>: it replaces that interface registration.
    /// </summary>
    public static IServiceCollection AddOccupancyCommitPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IWorkflowUnitOfWork>(sp => new EFWorkflowUnitOfWork(sp.GetRequiredService<ServiceOrderDbContext>()));
        services.Replace(ServiceDescriptor.Scoped<IOccupancyCommitService>(sp =>
            new ObservableOccupancyCommitService(
                sp.GetRequiredService<OccupancyCommitService>(),
                sp.GetRequiredService<GeoAssetsActivitySource>(),
                sp.GetRequiredService<GeoAssetsMeter>(),
                sp.GetService<TimeProvider>())));

        return services;
    }
}
