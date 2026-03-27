using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// DI registration helpers for the Apache Kafka messaging implementation.
/// </summary>
/// <example>
/// <b>appsettings.json</b>
/// <code>
/// {
///   "WorkflowKafka": {
///     "BootstrapServers": "broker1:9092,broker2:9092",
///     "TopicName"       : "order-state-changed",
///     "ClientId"        : "geoassets-api",
///     "UseOrderIdAsKey" : true,
///     "AdditionalConfig": {
///       "acks"            : "all",
///       "compression.type": "snappy"
///     }
///   }
/// }
/// </code>
///
/// <b>Program.cs — from configuration</b>
/// <code>
/// builder.Services.AddWorkflowKafka(builder.Configuration);
/// </code>
///
/// <b>Program.cs — inline</b>
/// <code>
/// builder.Services.AddWorkflowKafka(opts =>
/// {
///     opts.BootstrapServers = "broker1:9092,broker2:9092";
///     opts.TopicName        = "order-state-changed";
///     opts.ClientId         = "geoassets-api";
///     opts.AdditionalConfig["acks"] = "all";
/// });
/// </code>
///
/// <b>Consuming the event (Confluent.Kafka consumer)</b>
/// <code>
/// var consumer = new ConsumerBuilder&lt;string, string&gt;(new ConsumerConfig
/// {
///     BootstrapServers = "broker1:9092",
///     GroupId          = "notification-worker",
///     AutoOffsetReset  = AutoOffsetReset.Earliest,
/// }).Build();
///
/// consumer.Subscribe("order-state-changed");
///
/// while (!stoppingToken.IsCancellationRequested)
/// {
///     var result = consumer.Consume(stoppingToken);
///     var evt    = JsonSerializer.Deserialize&lt;OrderStateChangedEvent&gt;(result.Message.Value)!;
///     // evt.Metadata["recipients"] contains the comma-separated actor IDs
/// }
/// </code>
/// </example>
public static class WorkflowKafkaServiceExtensions
{
    /// <summary>
    /// Registers <see cref="KafkaOrderEventPublisher"/> as the
    /// <see cref="IOrderEventPublisher"/> and wires
    /// <see cref="OrderNotificationService"/> as <see cref="IOrderNotificationService"/>.
    ///
    /// Options are read from <c>configuration["WorkflowKafka"]</c>.
    ///
    /// <code>
    ///   builder.Services.AddWorkflowKafka(builder.Configuration);
    /// </code>
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
    ///
    /// <code>
    ///   builder.Services.AddWorkflowKafka(opts =>
    ///   {
    ///       opts.BootstrapServers = "broker1:9092,broker2:9092";
    ///       opts.TopicName        = "order-state-changed";
    ///   });
    /// </code>
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
}
