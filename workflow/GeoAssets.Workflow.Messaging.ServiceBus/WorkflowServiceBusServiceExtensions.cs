using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// DI registration helpers for the Azure Service Bus messaging implementation.
/// <see cref="ServiceBusOrderEventPublisher"/> requires a registered
/// <see cref="GeoAssets.Infrastructure.Observability.GeoAssetsActivitySource"/> —
/// call <c>builder.Services.AddGeoAssetsObservability(...)</c> before
/// <see cref="AddWorkflowServiceBus(IServiceCollection, IConfiguration)"/>.
/// </summary>
public static class WorkflowServiceBusServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ServiceBusOrderEventPublisher"/> as the
    /// <see cref="IOrderEventPublisher"/> and wires
    /// <see cref="OrderNotificationService"/> as <see cref="IOrderNotificationService"/>.
    ///
    /// Options are read from <c>configuration["WorkflowServiceBus"]</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowServiceBus(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<ServiceBusPublisherOptions>(opts =>
            configuration.GetSection(ServiceBusPublisherOptions.SectionName).Bind(opts));

        services.AddSingleton<IOrderEventPublisher,      ServiceBusOrderEventPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ServiceBusOrderEventPublisher"/> with inline options.
    /// </summary>
    public static IServiceCollection AddWorkflowServiceBus(
        this IServiceCollection          services,
        Action<ServiceBusPublisherOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<IOrderEventPublisher,      ServiceBusOrderEventPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ServiceBusIntegrationEventPublisher"/> as <see cref="IIntegrationEventPublisher"/>
    /// bound from <c>configuration["WorkflowServiceBus"]</c>.
    /// </summary>
    public static IServiceCollection AddServiceBusIntegrationPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ServiceBusPublisherOptions>(opts =>
            configuration.GetSection(ServiceBusPublisherOptions.SectionName).Bind(opts));
        services.AddSingleton<IIntegrationEventPublisher, ServiceBusIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ServiceBusIntegrationEventPublisher"/> as <see cref="IIntegrationEventPublisher"/>
    /// configured via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddServiceBusIntegrationPublisher(
        this IServiceCollection services,
        Action<ServiceBusPublisherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<IIntegrationEventPublisher, ServiceBusIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/> and <typeparamref name="THandler"/>
    /// bound from <c>configuration["WorkflowServiceBusConsumer"]</c>.
    /// </summary>
    public static IServiceCollection AddServiceBusIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<ServiceBusConsumerOptions>(opts =>
            configuration.GetSection(ServiceBusConsumerOptions.SectionName).Bind(opts));
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddHostedService<ServiceBusIntegrationEventConsumer<TEvent>>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/> and <typeparamref name="THandler"/>
    /// configured via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddServiceBusIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        Action<ServiceBusConsumerOptions> configure)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.Configure(configure);
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddHostedService<ServiceBusIntegrationEventConsumer<TEvent>>();

        return services;
    }

    /// <summary>
    /// The options of one named consumer (XD01-383): <c>WorkflowServiceBusConsumer</c>'s shared settings, overridden by its
    /// <c>Consumers:{<paramref name="consumerKey"/>}</c> sub-section, then by <paramref name="configure"/> — so several
    /// consumers of one host read the same topic, each on its own subscription and for its own message types.
    /// </summary>
    public static ServiceBusConsumerOptions BindNamedConsumerOptions(
        IConfiguration configuration,
        string consumerKey,
        Action<ServiceBusConsumerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);

        var options = new ServiceBusConsumerOptions();
        var shared = configuration.GetSection(ServiceBusConsumerOptions.SectionName);
        shared.Bind(options);
        shared.GetSection(ServiceBusConsumerOptions.ConsumersSectionName).GetSection(consumerKey).Bind(options);
        configure?.Invoke(options);
        return options;
    }

    /// <summary>
    /// Registers one named <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/> with <typeparamref name="THandler"/>
    /// (XD01-383) on the options <see cref="BindNamedConsumerOptions"/> builds — its own options instance, so it neither
    /// reads nor changes the host's default consumer's.
    /// </summary>
    public static IServiceCollection AddServiceBusIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerKey,
        Action<ServiceBusConsumerOptions>? configure)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = Options.Create(BindNamedConsumerOptions(configuration, consumerKey, configure));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddSingleton<IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<ServiceBusIntegrationEventConsumer<TEvent>>(sp, options));

        return services;
    }
}
