using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// DI registration helpers for the Apache Kafka messaging implementation.
/// <see cref="KafkaOrderEventPublisher"/> requires a registered
/// <see cref="GeoAssets.Infrastructure.Observability.GeoAssetsActivitySource"/> —
/// call <c>builder.Services.AddGeoAssetsObservability(...)</c> before
/// <see cref="AddWorkflowKafka(IServiceCollection, IConfiguration)"/>.
/// </summary>
public static class WorkflowKafkaServiceExtensions
{
    /// <summary>
    /// Registers <see cref="KafkaOrderEventPublisher"/> as the
    /// <see cref="IOrderEventPublisher"/> and wires
    /// <see cref="OrderNotificationService"/> as <see cref="IOrderNotificationService"/>.
    ///
    /// Options are read from <c>configuration["WorkflowKafka"]</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowKafka(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaPublisherOptions>(opts =>
            configuration.GetSection(KafkaPublisherOptions.SectionName).Bind(opts));

        services.AddSingleton<IOrderEventPublisher,      KafkaOrderEventPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="KafkaOrderEventPublisher"/> with inline options.
    /// </summary>
    public static IServiceCollection AddWorkflowKafka(
        this IServiceCollection       services,
        Action<KafkaPublisherOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<IOrderEventPublisher,      KafkaOrderEventPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="KafkaIntegrationEventPublisher"/> as <see cref="IIntegrationEventPublisher"/>
    /// bound from <c>configuration["WorkflowKafka"]</c>.
    /// </summary>
    public static IServiceCollection AddKafkaIntegrationPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KafkaPublisherOptions>(opts =>
            configuration.GetSection(KafkaPublisherOptions.SectionName).Bind(opts));
        services.AddSingleton<IIntegrationEventPublisher, KafkaIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="KafkaIntegrationEventPublisher"/> as <see cref="IIntegrationEventPublisher"/>
    /// configured via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddKafkaIntegrationPublisher(
        this IServiceCollection services,
        Action<KafkaPublisherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<IIntegrationEventPublisher, KafkaIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="KafkaIntegrationEventConsumer{TEvent}"/> and <typeparamref name="THandler"/>
    /// bound from <c>configuration["WorkflowKafkaConsumer"]</c>.
    /// </summary>
    public static IServiceCollection AddKafkaIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<KafkaConsumerOptions>(opts =>
            configuration.GetSection(KafkaConsumerOptions.SectionName).Bind(opts));
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddHostedService<KafkaIntegrationEventConsumer<TEvent>>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="KafkaIntegrationEventConsumer{TEvent}"/> and <typeparamref name="THandler"/>
    /// configured via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddKafkaIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        Action<KafkaConsumerOptions> configure)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.Configure(configure);
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddHostedService<KafkaIntegrationEventConsumer<TEvent>>();

        return services;
    }
}
