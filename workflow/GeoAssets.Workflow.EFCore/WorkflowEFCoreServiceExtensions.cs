using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Identity.Authorization;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                sp.GetService<IAssetProvider>(),
                sp.GetService<OrderTypeRegistry>(),
                sp.GetService<TimeProvider>()));
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
        services.AddScoped<IContractorUsLedgerRepository, EFContractorUsLedgerRepository>();
        services.AddServiceOrderTargetPersistence();

        return services;
    }

    /// <summary>
    /// Registers the EF Core store of service order targets (XD01-306) for both of its ports:
    /// <see cref="IServiceOrderTargetReader"/> and <see cref="IServiceOrderTargetWriter"/> resolve
    /// to the same scoped <see cref="EFServiceOrderTargetRepository"/>, which shares the scope's
    /// <see cref="ServiceOrderDbContext"/> with the order repository.
    ///
    /// Already called by <see cref="AddWorkflowPersistence"/>; a host that registers
    /// <see cref="ServiceOrderDbContext"/> itself calls this directly. <see cref="OrderTypeRegistry"/>
    /// and <see cref="TimeProvider"/> are used when registered (see the repository's constructor
    /// for what each absence means).
    /// </summary>
    public static IServiceCollection AddServiceOrderTargetPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped(sp => new EFServiceOrderTargetRepository(
            sp.GetRequiredService<ServiceOrderDbContext>(),
            sp.GetService<OrderTypeRegistry>(),
            sp.GetService<TimeProvider>()));
        services.TryAddScoped<IServiceOrderTargetReader>(sp => sp.GetRequiredService<EFServiceOrderTargetRepository>());
        services.TryAddScoped<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<EFServiceOrderTargetRepository>());

        return services;
    }

    /// <summary>
    /// Registers the EF Core transactional outbox (<see cref="IOutboxWriter"/>,
    /// <see cref="IOutboxDispatcherStore"/>), idempotent inbox (<see cref="IIdempotentInbox"/>),
    /// and background <see cref="OutboxRelayProcessor"/> hosted service.
    /// </summary>
    public static IServiceCollection AddWorkflowOutbox(
        this IServiceCollection services,
        Action<OutboxRelayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OutboxRelayOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddScoped(sp => new EFOutboxStore(
            sp.GetRequiredService<ServiceOrderDbContext>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IIntegrationMessageSerializer>()));
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<EFOutboxStore>());
        services.AddScoped<IOutboxDispatcherStore>(sp => sp.GetRequiredService<EFOutboxStore>());
        services.AddScoped<IIdempotentInbox, EFIdempotentInbox>();
        services.AddHostedService<OutboxRelayProcessor>();

        return services;
    }

    /// <summary>
    /// Registers the background <see cref="OrganizationGrantSyncWorker"/> hosted service
    /// (XD01-290) that mirrors <c>OrganizationGrant</c> changes into this database — the host
    /// must separately register an <see cref="IOrganizationGrantSyncClient"/> implementation
    /// (e.g. an HTTP client calling back to the Core Platform's identity host).
    /// </summary>
    public static IServiceCollection AddOrganizationGrantSync(
        this IServiceCollection services,
        Action<OrganizationGrantSyncOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OrganizationGrantSyncOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHostedService<OrganizationGrantSyncWorker>();

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
        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<OrderTypeRegistry>();
        var orderTypeRepo = scope.ServiceProvider.GetRequiredService<IOrderTypeRepository>();
        await registry.LoadFromAsync(orderTypeRepo, ct).ConfigureAwait(false);
    }
}
