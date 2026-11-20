using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// The options of one named consumer (XD01-383): <c>WorkflowKafkaConsumer</c>'s shared settings, overridden by its
    /// <c>Consumers:{<paramref name="consumerKey"/>}</c> sub-section, then by <paramref name="configure"/> — so several
    /// consumers of one host read the same topic, each in its own group and for its own message types.
    /// </summary>
    public static KafkaConsumerOptions BindNamedConsumerOptions(
        IConfiguration configuration,
        string consumerKey,
        Action<KafkaConsumerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);

        var options = new KafkaConsumerOptions();
        var shared = configuration.GetSection(KafkaConsumerOptions.SectionName);
        shared.Bind(options);
        shared.GetSection(KafkaConsumerOptions.ConsumersSectionName).GetSection(consumerKey).Bind(options);
        configure?.Invoke(options);
        return options;
    }

    /// <summary>
    /// Registers one named <see cref="KafkaIntegrationEventConsumer{TEvent}"/> with <typeparamref name="THandler"/>
    /// (XD01-383) on the options <see cref="BindNamedConsumerOptions"/> builds — its own options instance, so it neither
    /// reads nor changes the host's default consumer's.
    /// </summary>
    public static IServiceCollection AddKafkaIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerKey,
        Action<KafkaConsumerOptions>? configure)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = Options.Create(BindNamedConsumerOptions(configuration, consumerKey, configure));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddSingleton<IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<KafkaIntegrationEventConsumer<TEvent>>(sp, options));

        return services;
    }
}
