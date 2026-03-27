using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// DI registration helpers for the Azure Service Bus messaging implementation.
/// </summary>
/// <example>
/// <b>appsettings.json</b>
/// <code>
/// {
///   "WorkflowServiceBus": {
///     "ConnectionString": "Endpoint=sb://my-ns.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=...",
///     "TopicName"       : "order-state-changed",
///     "MessageSubject"  : "OrderStateChanged"
///   }
/// }
/// </code>
///
/// <b>Program.cs — from configuration</b>
/// <code>
/// builder.Services.AddWorkflowServiceBus(builder.Configuration);
/// </code>
///
/// <b>Program.cs — inline (e.g. from Key Vault / environment variable)</b>
/// <code>
/// builder.Services.AddWorkflowServiceBus(opts =>
/// {
///     opts.ConnectionString = Environment.GetEnvironmentVariable("SB_CONNSTR")!;
///     opts.TopicName        = "order-state-changed";
/// });
/// </code>
///
/// <b>Consuming the event in a Service Bus trigger (Azure Functions / Worker)</b>
/// <code>
/// [Function("HandleOrderStateChanged")]
/// public Task RunAsync(
///     [ServiceBusTrigger("order-state-changed", "my-subscription",
///                         Connection = "SB_CONNSTR")] ServiceBusReceivedMessage msg,
///     FunctionContext ctx)
/// {
///     var evt = JsonSerializer.Deserialize&lt;OrderStateChangedEvent&gt;(msg.Body)!;
///     // fan-out to individual recipients via evt.Metadata["recipients"]
///     return Task.CompletedTask;
/// }
/// </code>
/// </example>
public static class WorkflowServiceBusServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ServiceBusOrderEventPublisher"/> as the
    /// <see cref="IOrderEventPublisher"/> and wires
    /// <see cref="OrderNotificationService"/> as <see cref="IOrderNotificationService"/>.
    ///
    /// Options are read from <c>configuration["WorkflowServiceBus"]</c>.
    ///
    /// <code>
    ///   builder.Services.AddWorkflowServiceBus(builder.Configuration);
    /// </code>
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
    ///
    /// <code>
    ///   builder.Services.AddWorkflowServiceBus(opts =>
    ///   {
    ///       opts.ConnectionString = "Endpoint=sb://...";
    ///       opts.TopicName        = "order-state-changed";
    ///   });
    /// </code>
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
}
