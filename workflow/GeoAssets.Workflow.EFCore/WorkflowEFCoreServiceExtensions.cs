using GeoAssets.Core.Interfaces;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Workflow;

/// <summary>
/// DI registration helpers for the GeoAssets workflow layer.
/// </summary>
public static class WorkflowEFCoreServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ServiceOrderDbContext"/> and the EF Core implementation of
    /// <see cref="IServiceOrderRepository"/>.
    ///
    /// <paramref name="configureDb"/> should set the database provider, e.g.:
    /// <code>
    ///   services.AddWorkflowPersistence(o =>
    ///       o.UseSqlServer(configuration.GetConnectionString("Workflow")));
    /// </code>
    ///
    /// If an <see cref="IAssetProvider"/> is already registered in the container it
    /// will be injected automatically to hydrate features on order load.
    ///
    /// The registered <see cref="IServiceOrderRepository"/> is wrapped in
    /// <see cref="ObservableServiceOrderRepository"/> (tracing/metrics/logging for status
    /// transitions), which requires <see cref="GeoAssetsActivitySource"/>/<see cref="GeoAssetsMeter"/>
    /// to already be registered — call <c>AddGeoAssetsObservability</c> before this method.
    /// </summary>
    public static IServiceCollection AddWorkflowPersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<ServiceOrderDbContext>(configureDb);

        services.AddScoped(sp =>
            new EFServiceOrderRepository(
                sp.GetRequiredService<ServiceOrderDbContext>(),
                sp.GetService<IAssetProvider>()));
        services.AddScoped<IServiceOrderRepository>(sp =>
            new ObservableServiceOrderRepository(
                new ValidatingServiceOrderRepository(
                    sp.GetRequiredService<EFServiceOrderRepository>(),
                    sp.GetService<OrderTypeRegistry>()),
                sp.GetRequiredService<GeoAssetsActivitySource>(),
                sp.GetRequiredService<GeoAssetsMeter>(),
                sp.GetRequiredService<ILogger<ObservableServiceOrderRepository>>()));

        // Read-only or write-only consumers can depend on just the piece they need
        // instead of the full IServiceOrderRepository.
        services.AddScoped<IServiceOrderReader>(sp => sp.GetRequiredService<IServiceOrderRepository>());
        services.AddScoped<IServiceOrderWriter>(sp => sp.GetRequiredService<IServiceOrderRepository>());

        services.AddScoped<IOrderTypeRepository, EFOrderTypeRepository>();

        return services;
    }

    /// <summary>
    /// Loads all <see cref="OrderType"/> rows from the database into the
    /// <see cref="OrderTypeRegistry"/> singleton.
    ///
    /// Call once after <c>host.Build()</c>:
    /// <code>
    ///   var host = builder.Build();
    ///   await host.Services.LoadRegistryFromDbAsync();
    ///   await host.RunAsync();
    /// </code>
    /// </summary>
    public static async Task LoadRegistryFromDbAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        using var scope      = services.CreateScope();
        var registry         = scope.ServiceProvider.GetRequiredService<OrderTypeRegistry>();
        var orderTypeRepo    = scope.ServiceProvider.GetRequiredService<IOrderTypeRepository>();
        await registry.LoadFromAsync(orderTypeRepo, ct);
    }
}
